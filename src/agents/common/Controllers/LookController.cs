using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using AgentCommon.Attributes;
using AgentCommon.Models;

namespace AgentCommon.Controllers;

/// <summary>
/// Controller for listing files and directories
/// </summary>
[ApiController]
[RequireAgentToken]
public class LookController : ControllerBase
{
    private readonly ILogger<LookController> _logger;

    public LookController(ILogger<LookController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// List files and directories in the specified directory
    /// </summary>
    /// <param name="dir">Directory path to list (query parameter)</param>
    /// <returns>List of files and directories</returns>
    [HttpGet("/Look")]
    public IActionResult ListDirectory([FromQuery] string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            return BadRequest(new { message = "Directory path is required. Use ?dir=/path/to/directory" });
        }

        try
        {
            var directoryPath = dir.Trim();

            // Security check: prevent directory traversal attacks
            if (directoryPath.Contains(".."))
            {
                _logger.LogWarning("Potentially unsafe directory path requested: {Path}", directoryPath);
                return BadRequest(new { message = "Invalid directory path: directory traversal (..) is not allowed" });
            }

            // Check if directory exists
            if (!Directory.Exists(directoryPath))
            {
                _logger.LogWarning("Directory not found: {Path}", directoryPath);
                return NotFound(new { message = $"Directory not found: {directoryPath}" });
            }

            // Get directory info
            var directoryInfo = new DirectoryInfo(directoryPath);
            
            // Get all items (directories and files) recursively in a single list
            var items = new List<FileSystemItem>();

            try
            {
                // Recursively get all directories and files
                GetAllDirectoriesAndFiles(directoryInfo, items);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Access denied to directory: {Path}", directoryPath);
                return StatusCode(403, new { message = $"Access denied to directory: {directoryPath}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading directory: {Path}", directoryPath);
                return StatusCode(500, new { message = $"Error reading directory: {ex.Message}" });
            }

            var dirCount = items.Count(i => i.Type == "directory");
            var fileCount = items.Count(i => i.Type == "file");

            _logger.LogInformation("Listed directory: {Path}, {DirCount} directories, {FileCount} files", 
                directoryPath, dirCount, fileCount);

            return Ok(items.OrderBy(i => i.Path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing directory listing request: {Path}", dir);
            return StatusCode(500, new { message = $"An error occurred: {ex.Message}" });
        }
    }

    private void GetAllDirectoriesAndFiles(DirectoryInfo directory, List<FileSystemItem> items)
    {
        // Get immediate subdirectories
        DirectoryInfo[] subdirectories;
        try
        {
            subdirectories = directory.GetDirectories();
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Access denied to directory: {Path}", directory.FullName);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting subdirectories from: {Path}", directory.FullName);
            return;
        }

        foreach (var dirInfo in subdirectories)
        {
            try
            {
                items.Add(new FileSystemItem
                {
                    Name = dirInfo.Name,
                    Path = dirInfo.FullName,
                    Type = "directory",
                    Size = null,
                    LastModified = dirInfo.LastWriteTimeUtc,
                    Permissions = GetUnixPermissions(dirInfo.FullName)
                });

                // Recursively process subdirectories - this is the key recursive call
                GetAllDirectoriesAndFiles(dirInfo, items);
            }
            catch (Exception ex)
            {
                // Log but continue processing other directories
                _logger.LogWarning(ex, "Error processing subdirectory: {Path}", dirInfo.FullName);
                // Continue to next directory
            }
        }

        // Get files in current directory
        FileInfo[] files;
        try
        {
            files = directory.GetFiles();
        }
        catch (UnauthorizedAccessException)
        {
            // Can't read files, but we already processed subdirectories, so just return
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting files from: {Path}", directory.FullName);
            return;
        }

        foreach (var fileInfo in files)
        {
            try
            {
                // Calculate MD5 hash for the file
                var md5Hash = CalculateFileMd5(fileInfo.FullName);

                items.Add(new FileSystemItem
                {
                    Name = fileInfo.Name,
                    Path = fileInfo.FullName,
                    Type = "file",
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    Permissions = GetUnixPermissions(fileInfo.FullName),
                    Md5 = md5Hash
                });
            }
            catch (Exception ex)
            {
                // Log but continue processing other files
                _logger.LogWarning(ex, "Error processing file: {Path}", fileInfo.FullName);
                // Continue to next file
            }
        }
    }

    private string? CalculateFileMd5(string filePath)
    {
        try
        {
            using var md5 = MD5.Create();
            using var stream = System.IO.File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating MD5 for file: {Path}", filePath);
            return null;
        }
    }

    private string? GetUnixPermissions(string path)
    {
        try
        {
            // On Unix-like systems, get file permissions using stat command
            // This works for both files and directories
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return GetUnixPermissionsViaStat(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting Unix permissions for: {Path}", path);
        }
        return null;
    }

    private string? GetUnixPermissionsViaStat(string path)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "stat",
                Arguments = $"-c %a \"{path}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
                return null;

            process.WaitForExit(1000); // 1 second timeout

            if (process.ExitCode == 0)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                // stat returns 4 digits sometimes (e.g., "0755"), we want 3 digits
                if (output.Length >= 3)
                {
                    return output.Substring(output.Length - 3);
                }
                return output;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting Unix permissions via stat for: {Path}", path);
        }
        return null;
    }
}

