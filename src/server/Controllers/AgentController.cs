using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using server.Data;
using server.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentController : ControllerBase
{
    private readonly DBContext _context;
    private readonly ILogger<AgentController> _logger;
    private readonly IWebHostEnvironment _environment;

    public AgentController(DBContext context, ILogger<AgentController> logger, IWebHostEnvironment environment)
    {
        _context = context;
        _logger = logger;
        _environment = environment;
    }

    [HttpPost]
    [ProducesResponseType(typeof(Agent), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateAgent([FromBody] CreateAgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Hostname))
        {
            return BadRequest(new { message = "Hostname is required" });
        }

        var hostname = request.Hostname.Trim();

        // Create agent with rsync configuration (no token needed for rsync)
        var agent = new Agent
        {
            id = Guid.NewGuid(),
            name = string.IsNullOrWhiteSpace(request.Name) ? "New Agent" : request.Name.Trim(),
            hostname = hostname,
            token = null, // Token not needed for rsync-based agents
            rsyncUser = request.RsyncUser?.Trim(),
            rsyncPort = request.RsyncPort ?? 22,
            rsyncSshKey = request.RsyncSshKey?.Trim()
        };

        try
        {
            _context.Agents.Add(agent);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Agent created with ID: {AgentId}, Hostname: {Hostname}", agent.id, agent.hostname);

            return CreatedAtAction(
                nameof(GetAgent),
                new { id = agent.id },
                agent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating agent with hostname: {Hostname}", hostname);
            return StatusCode(500, new { message = "An error occurred while creating the agent" });
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<Agent>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAgents()
    {
        try
        {
            var agents = await _context.Agents.ToListAsync();
            return Ok(agents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agents");
            return StatusCode(500, new { message = "An error occurred while retrieving agents" });
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Agent), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAgent(Guid id)
    {
        var agent = await _context.Agents.FindAsync(id);

        if (agent == null)
        {
            return NotFound(new { message = "Agent not found" });
        }

        return Ok(agent);
    }

    [HttpPost("validate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ValidateSshConnection([FromBody] ValidateSshConnectionRequest request)
    {
        try
        {
            // Check if required fields are present
            if (string.IsNullOrWhiteSpace(request.Hostname))
            {
                return BadRequest(new { message = "Hostname is required" });
            }

            // Check if SSH key is configured
            if (string.IsNullOrWhiteSpace(request.RsyncSshKey))
            {
                return BadRequest(new { message = "SSH private key is required to validate the connection." });
            }

            int port = (request.RsyncPort > 0) ? request.RsyncPort : 22;
            var user = string.IsNullOrWhiteSpace(request.RsyncUser) ? "" : $"{request.RsyncUser}@";

            // Create temporary SSH key file
            string? tempSshKeyFile = null;
            try
            {
                tempSshKeyFile = CreateTempSshKeyFile(request.RsyncSshKey);

                // Test SSH connection with a simple command (echo test)
                var sshCommand = "echo 'SSH connection test successful'";
                var sshArgs = $"-i \"{tempSshKeyFile}\" -p {port} -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o ConnectTimeout=10 {user}{request.Hostname.Trim()} {sshCommand}";

                _logger.LogInformation("Validating SSH connection: ssh {Args}", sshArgs);

                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ssh",
                    Arguments = sshArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processStartInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start SSH process");
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    _logger.LogError("SSH connection validation failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, error);
                    
                    // Provide more specific error messages
                    string errorMessage;
                    if (error.Contains("Permission denied") || error.Contains("publickey"))
                    {
                        errorMessage = "SSH authentication failed. Please verify that:\n" +
                                     "- The SSH private key is correct\n" +
                                     "- The corresponding public key is installed on the remote server\n" +
                                     "- The SSH user has permission to access the server";
                    }
                    else if (error.Contains("Connection refused") || error.Contains("Connection timed out"))
                    {
                        errorMessage = $"SSH connection failed. Unable to connect to {request.Hostname.Trim()}:{port}.\n" +
                                     "Please verify that:\n" +
                                     "- The hostname and port are correct\n" +
                                     "- The SSH service is running on the remote server\n" +
                                     "- The server is reachable from this machine\n" +
                                     "- Firewall rules allow SSH connections";
                    }
                    else if (error.Contains("Host key verification failed"))
                    {
                        errorMessage = "SSH host key verification failed. This is handled automatically, but if the problem persists, please check the server's host key.";
                    }
                    else if (error.Contains("No route to host") || error.Contains("Network is unreachable"))
                    {
                        errorMessage = $"Network error: Unable to reach {request.Hostname.Trim()}.\n" +
                                     "Please verify that:\n" +
                                     "- The hostname is correct\n" +
                                     "- The server is online and reachable\n" +
                                     "- Network connectivity is available";
                    }
                    else
                    {
                        errorMessage = $"SSH connection failed: {error.Trim()}\n" +
                                     $"Exit code: {process.ExitCode}";
                    }

                    return StatusCode(503, new { message = errorMessage });
                }

                _logger.LogInformation("SSH connection validation successful to {Hostname}", request.Hostname.Trim());

                return Ok(new { 
                    message = $"SSH connection successful to {request.Hostname.Trim()}:{port}", 
                    hostname = request.Hostname.Trim(),
                    port = port,
                    user = request.RsyncUser ?? "default",
                    hasSshKey = true,
                    rsyncUser = request.RsyncUser,
                    rsyncPort = request.RsyncPort
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating SSH connection");
                
                // Provide user-friendly error message
                string errorMessage;
                if (ex.Message.Contains("No such file or directory") || ex.Message.Contains("ssh: command not found"))
                {
                    errorMessage = "SSH client is not available on this system. Please install OpenSSH client.";
                }
                else if (ex.Message.Contains("Failed to start SSH process"))
                {
                    errorMessage = "Failed to start SSH process. Please ensure SSH client is properly installed and accessible.";
                }
                else
                {
                    errorMessage = $"Error testing SSH connection: {ex.Message}";
                }

                return StatusCode(500, new { message = errorMessage });
            }
            finally
            {
                // Clean up temporary SSH key file
                if (!string.IsNullOrWhiteSpace(tempSshKeyFile) && System.IO.File.Exists(tempSshKeyFile))
                {
                    try
                    {
                        System.IO.File.Delete(tempSshKeyFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temporary SSH key file: {Path}", tempSshKeyFile);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating SSH connection");
            return StatusCode(500, new { message = $"An error occurred while validating the SSH connection: {ex.Message}" });
        }
    }

    [HttpPost("{id}/validate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ValidateAgent(Guid id)
    {
        try
        {
            var agent = await _context.Agents.FindAsync(id);

            if (agent == null)
            {
                return NotFound(new { message = "Agent not found" });
            }

            // Check if SSH key is configured
            if (string.IsNullOrWhiteSpace(agent.rsyncSshKey))
            {
                return BadRequest(new { message = "Agent does not have SSH key configured. Please add an SSH private key to validate the connection." });
            }

            // Check if required fields are present
            if (string.IsNullOrWhiteSpace(agent.hostname))
            {
                return BadRequest(new { message = "Agent hostname is required" });
            }

            int port = (agent.rsyncPort > 0) ? agent.rsyncPort : 22;
            var user = string.IsNullOrWhiteSpace(agent.rsyncUser) ? "" : $"{agent.rsyncUser}@";

            // Create temporary SSH key file
            string? tempSshKeyFile = null;
            try
            {
                tempSshKeyFile = CreateTempSshKeyFile(agent.rsyncSshKey);

                // Test SSH connection with a simple command (echo test)
                var sshCommand = "echo 'SSH connection test successful'";
                var sshArgs = $"-i \"{tempSshKeyFile}\" -p {port} -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -o ConnectTimeout=10 {user}{agent.hostname} {sshCommand}";

                _logger.LogInformation("Validating SSH connection: ssh {Args}", sshArgs);

                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ssh",
                    Arguments = sshArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processStartInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start SSH process");
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    _logger.LogError("SSH connection validation failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, error);
                    
                    // Provide more specific error messages
                    string errorMessage;
                    if (error.Contains("Permission denied") || error.Contains("publickey"))
                    {
                        errorMessage = "SSH authentication failed. Please verify that:\n" +
                                     "- The SSH private key is correct\n" +
                                     "- The corresponding public key is installed on the remote server\n" +
                                     "- The SSH user has permission to access the server";
                    }
                    else if (error.Contains("Connection refused") || error.Contains("Connection timed out"))
                    {
                        errorMessage = $"SSH connection failed. Unable to connect to {agent.hostname}:{port}.\n" +
                                     "Please verify that:\n" +
                                     "- The hostname and port are correct\n" +
                                     "- The SSH service is running on the remote server\n" +
                                     "- The server is reachable from this machine\n" +
                                     "- Firewall rules allow SSH connections";
                    }
                    else if (error.Contains("Host key verification failed"))
                    {
                        errorMessage = "SSH host key verification failed. This is handled automatically, but if the problem persists, please check the server's host key.";
                    }
                    else if (error.Contains("No route to host") || error.Contains("Network is unreachable"))
                    {
                        errorMessage = $"Network error: Unable to reach {agent.hostname}.\n" +
                                     "Please verify that:\n" +
                                     "- The hostname is correct\n" +
                                     "- The server is online and reachable\n" +
                                     "- Network connectivity is available";
                    }
                    else
                    {
                        errorMessage = $"SSH connection failed: {error.Trim()}\n" +
                                     $"Exit code: {process.ExitCode}";
                    }

                    return StatusCode(503, new { message = errorMessage });
                }

                _logger.LogInformation("SSH connection validation successful for agent {AgentId} to {Hostname}", id, agent.hostname);

                return Ok(new { 
                    message = $"SSH connection successful to {agent.hostname}:{port}", 
                    hostname = agent.hostname,
                    port = port,
                    user = agent.rsyncUser ?? "default",
                    hasSshKey = true,
                    rsyncUser = agent.rsyncUser,
                    rsyncPort = agent.rsyncPort
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating SSH connection for agent {AgentId}", id);
                
                // Provide user-friendly error message
                string errorMessage;
                if (ex.Message.Contains("No such file or directory") || ex.Message.Contains("ssh: command not found"))
                {
                    errorMessage = "SSH client is not available on this system. Please install OpenSSH client.";
                }
                else if (ex.Message.Contains("Failed to start SSH process"))
                {
                    errorMessage = "Failed to start SSH process. Please ensure SSH client is properly installed and accessible.";
                }
                else
                {
                    errorMessage = $"Error testing SSH connection: {ex.Message}";
                }

                return StatusCode(500, new { message = errorMessage });
            }
            finally
            {
                // Clean up temporary SSH key file
                if (!string.IsNullOrWhiteSpace(tempSshKeyFile) && System.IO.File.Exists(tempSshKeyFile))
                {
                    try
                    {
                        System.IO.File.Delete(tempSshKeyFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temporary SSH key file: {Path}", tempSshKeyFile);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating agent {AgentId}", id);
            return StatusCode(500, new { message = $"An error occurred while validating the agent: {ex.Message}" });
        }
    }

    [HttpGet("{id}/ssh-key")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAgentSshKey(Guid id)
    {
        try
        {
            var agent = await _context.Agents.FindAsync(id);

            if (agent == null)
            {
                return NotFound(new { message = "Agent not found" });
            }

            return Ok(new { 
                id = agent.id,
                name = agent.name,
                hostname = agent.hostname,
                rsyncSshKey = agent.rsyncSshKey ?? string.Empty,
                hasSshKey = !string.IsNullOrWhiteSpace(agent.rsyncSshKey)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving SSH key for agent {AgentId}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving the SSH key" });
        }
    }

    [HttpGet("{id}/browse")]
    [ProducesResponseType(typeof(List<FileSystemItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BrowseAgentFileSystem(Guid id, [FromQuery] string? dir)
    {
        try
        {
            var agent = await _context.Agents.FindAsync(id);

            if (agent == null)
            {
                return NotFound(new { message = "Agent not found" });
            }

            if (string.IsNullOrWhiteSpace(agent.rsyncSshKey))
            {
                return BadRequest(new { message = "Agent does not have SSH key configured" });
            }

            // Default to root directory if not provided
            var directoryPath = string.IsNullOrWhiteSpace(dir) ? "/" : dir.Trim();

            // Security check: prevent directory traversal attacks
            if (directoryPath.Contains(".."))
            {
                _logger.LogWarning("Potentially unsafe directory path requested: {Path}", directoryPath);
                return BadRequest(new { message = "Invalid directory path: directory traversal (..) is not allowed" });
            }

            // Create temporary SSH key file
            string? tempSshKeyFile = null;
            try
            {
                tempSshKeyFile = CreateTempSshKeyFile(agent.rsyncSshKey);

                // Build SSH command to list files
                var user = string.IsNullOrWhiteSpace(agent.rsyncUser) ? "" : $"{agent.rsyncUser}@";
                var sshCommand = $"ls -la --time-style=full-iso \"{directoryPath}\"";
                var sshArgs = $"-i \"{tempSshKeyFile}\" -p {agent.rsyncPort} -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null {user}{agent.hostname} {sshCommand}";

                _logger.LogInformation("Executing SSH command: ssh {Args}", sshArgs);

                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ssh",
                    Arguments = sshArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processStartInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start SSH process");
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    _logger.LogError("SSH command failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
                    return StatusCode(500, new { message = $"SSH connection failed: {error}" });
                }

                // Parse ls -la output
                var items = ParseLsOutput(output, directoryPath);

                _logger.LogInformation("Browsed directory via SSH: {Path}, {Count} items", directoryPath, items.Count);

                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error browsing file system via SSH for agent {AgentId}", id);
                return StatusCode(500, new { message = $"Error browsing file system: {ex.Message}" });
            }
            finally
            {
                // Clean up temporary SSH key file
                if (!string.IsNullOrWhiteSpace(tempSshKeyFile) && System.IO.File.Exists(tempSshKeyFile))
                {
                    try
                    {
                        System.IO.File.Delete(tempSshKeyFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temporary SSH key file: {Path}", tempSshKeyFile);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error browsing file system for agent {AgentId}", id);
            return StatusCode(500, new { message = "An error occurred while browsing the file system" });
        }
    }

    private string NormalizeSshKeyContent(string sshKeyContent)
    {
        if (string.IsNullOrWhiteSpace(sshKeyContent))
        {
            return sshKeyContent;
        }
        
        // Trim leading and trailing whitespace
        var normalized = sshKeyContent.Trim();
        
        // Replace Windows line endings (CRLF) with Unix line endings (LF)
        normalized = normalized.Replace("\r\n", "\n");
        
        // Replace any remaining carriage returns with newlines
        normalized = normalized.Replace("\r", "\n");
        
        // Ensure the key ends with a newline (SSH keys typically should)
        if (!normalized.EndsWith("\n"))
        {
            normalized += "\n";
        }
        
        return normalized;
    }

    private string CreateTempSshKeyFile(string sshKeyContent)
    {
        try
        {
            // Create a temporary file for the SSH key
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "remember_ssh_keys");
            if (!System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.CreateDirectory(tempDir);
            }

            var tempKeyFile = System.IO.Path.Combine(tempDir, $"ssh_key_{Guid.NewGuid()}");
            
            // Normalize SSH key content: convert CRLF to LF, trim whitespace, ensure it ends with newline
            var normalizedKey = NormalizeSshKeyContent(sshKeyContent);
            
            // Write with UTF-8 encoding without BOM
            System.IO.File.WriteAllText(tempKeyFile, normalizedKey, new System.Text.UTF8Encoding(false));
            
            // Set proper permissions (600) on Unix-like systems
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try
                {
                    var processStartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"600 \"{tempKeyFile}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = System.Diagnostics.Process.Start(processStartInfo);
                    process?.WaitForExit();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set permissions on temporary SSH key file");
                }
            }

            return tempKeyFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create temporary SSH key file");
            throw;
        }
    }

    private List<FileSystemItem> ParseLsOutput(string output, string basePath)
    {
        var items = new List<FileSystemItem>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Normalize base path
        var normalizedBasePath = basePath.TrimEnd('/');
        if (string.IsNullOrEmpty(normalizedBasePath))
        {
            normalizedBasePath = "/";
        }

        foreach (var line in lines)
        {
            // Skip total line and empty lines
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("total"))
            {
                continue;
            }

            try
            {
                // Parse ls -la output format:
                // drwxr-xr-x 2 user group 4096 2024-01-01 12:00:00.000000000 +0000 dirname
                // -rw-r--r-- 1 user group 1024 2024-01-01 12:00:00.000000000 +0000 filename
                
                // Split by spaces but preserve the structure
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 9)
                {
                    continue;
                }

                var permissions = parts[0];
                var type = permissions[0] == 'd' ? "directory" : "file";
                
                // The name starts at index 8, but can contain spaces
                // We need to find where the date/time ends and the name begins
                // Format: permissions links owner group size month day time/year name
                // With --time-style=full-iso: permissions links owner group size YYYY-MM-DD HH:MM:SS.nnnnnnnnn +TZ name
                
                // Find the index where the name starts (after date/time)
                // Date format: YYYY-MM-DD HH:MM:SS.nnnnnnnnn +TZ (3 parts) or YYYY-MM-DD HH:MM:SS (2 parts)
                int nameStartIndex = 8; // Default: after size (index 4) + month (5) + day (6) + time (7) = 8
                
                // With --time-style=full-iso, we have: YYYY-MM-DD at index 5, HH:MM:SS.nnnnnnnnn at 6, +TZ at 7
                // So name starts at index 8
                // But if the time format is different, we need to adjust
                if (parts.Length > 8)
                {
                    // Check if index 5 looks like a date (YYYY-MM-DD)
                    if (parts[5].Contains("-") && parts[5].Length == 10)
                    {
                        // ISO format: name starts at index 8 (after YYYY-MM-DD HH:MM:SS.nnnnnnnnn +TZ)
                        nameStartIndex = 8;
                    }
                    else
                    {
                        // Traditional format: name starts at index 8 (after month day time)
                        nameStartIndex = 8;
                    }
                }

                var name = string.Join(" ", parts.Skip(nameStartIndex)); // Name can contain spaces

                // Skip . and .. entries
                if (name == "." || name == "..")
                {
                    continue;
                }

                // Parse size (for files)
                long? size = null;
                if (type == "file" && long.TryParse(parts[4], out var fileSize))
                {
                    size = fileSize;
                }

                // Parse date/time
                DateTime lastModified = DateTime.UtcNow;
                if (parts.Length >= 8)
                {
                    // Try to parse ISO format date: 2024-01-01 12:00:00.000000000 +0000
                    if (parts[5].Contains("-") && parts[5].Length == 10)
                    {
                        // ISO format
                        var dateStr = $"{parts[5]} {parts[6]}";
                        if (parts.Length > 7 && parts[7].StartsWith("+") || parts[7].StartsWith("-"))
                        {
                            dateStr += $" {parts[7]}";
                        }
                        if (DateTime.TryParse(dateStr, out var parsedDate))
                        {
                            lastModified = parsedDate.ToUniversalTime();
                        }
                    }
                    else
                    {
                        // Traditional format: try to parse month day time
                        // This is more complex, so we'll use current time as fallback
                    }
                }

                // Build full path - ensure proper path separators
                var fullPath = normalizedBasePath == "/" 
                    ? $"/{name}" 
                    : $"{normalizedBasePath}/{name}";
                
                // Normalize path (remove double slashes)
                fullPath = fullPath.Replace("//", "/");

                items.Add(new FileSystemItem
                {
                    Name = name,
                    PathName = fullPath,
                    Path = normalizedBasePath,
                    Type = type,
                    Size = size,
                    LastModified = lastModified,
                    Permissions = permissions,
                    Md5 = null // Don't calculate MD5 for browsing (too slow)
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing ls output line: {Line}", line);
            }
        }

        // Sort: directories first, then files, both alphabetically
        return items
            .OrderBy(i => i.Type == "file") // Directories first
            .ThenBy(i => i.Name)
            .ToList();
    }


    [HttpPut("{id}")]
    [ProducesResponseType(typeof(Agent), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAgent(Guid id, [FromBody] UpdateAgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Hostname))
        {
            return BadRequest(new { message = "Hostname is required" });
        }

        try
        {
            var agent = await _context.Agents.FindAsync(id);

            if (agent == null)
            {
                return NotFound(new { message = "Agent not found" });
            }

            // Update hostname and name
            agent.hostname = request.Hostname.Trim();
            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                agent.name = request.Name.Trim();
            }
            
            // Update rsync connection details if provided
            if (request.RsyncUser != null)
            {
                agent.rsyncUser = request.RsyncUser.Trim();
            }
            if (request.RsyncPort.HasValue)
            {
                agent.rsyncPort = request.RsyncPort.Value;
            }
            if (request.RsyncSshKey != null)
            {
                agent.rsyncSshKey = request.RsyncSshKey.Trim();
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Agent updated with ID: {AgentId}, Hostname: {Hostname}", 
                agent.id, agent.hostname);

            return Ok(agent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent {AgentId}", id);
            return StatusCode(500, new { message = "An error occurred while updating the agent" });
        }
    }


    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAgent(Guid id)
    {
        try
        {
            var agent = await _context.Agents.FindAsync(id);

            if (agent == null)
            {
                return NotFound(new { message = "Agent not found" });
            }

            // Delete all backup plans associated with this agent
            var backupPlans = await _context.BackupPlans
                .Where(bp => EF.Property<Guid?>(bp, "agentid") == id)
                .ToListAsync();

            if (backupPlans.Any())
            {
                _context.BackupPlans.RemoveRange(backupPlans);
                _logger.LogInformation("Deleting {Count} backup plans for agent {AgentId}", backupPlans.Count, id);
            }

            // Delete the agent
            _context.Agents.Remove(agent);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Agent deleted with ID: {AgentId}, Hostname: {Hostname}", agent.id, agent.hostname);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent {AgentId}", id);
            return StatusCode(500, new { message = "An error occurred while deleting the agent" });
        }
    }

    public class CreateAgentRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string? RsyncUser { get; set; }
        public int? RsyncPort { get; set; }
        public string? RsyncSshKey { get; set; }
    }

    public class UpdateAgentRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string? RsyncUser { get; set; }
        public int? RsyncPort { get; set; }
        public string? RsyncSshKey { get; set; }
    }

    public class ValidateSshConnectionRequest
    {
        public string Hostname { get; set; } = string.Empty;
        public string? RsyncUser { get; set; }
        public int RsyncPort { get; set; } = 22;
        public string? RsyncSshKey { get; set; }
    }
}

