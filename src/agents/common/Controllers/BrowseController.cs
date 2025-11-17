using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using AgentCommon.Attributes;
using AgentCommon.Models;

namespace AgentCommon.Controllers;

/// <summary>
/// Controller for browsing the file system (non-recursive directory listing)
/// </summary>
[ApiController]
[RequireAgentToken]
public class BrowseController : ControllerBase
{
    private readonly ILogger<BrowseController> _logger;

    public BrowseController(ILogger<BrowseController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Browse files and directories in the specified directory (non-recursive)
    /// </summary>
    /// <param name="dir">Directory path to browse (query parameter). Defaults to "/" if not provided.</param>
    /// <returns>List of immediate files and directories in the specified path</returns>
    [HttpGet("/Browse")]
    public IActionResult BrowseDirectory([FromQuery] string? dir)
    {
        // Default to root if not provided
        var directoryPath = string.IsNullOrWhiteSpace(dir) ? "/" : dir.Trim();

        try
        {
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
            
            // Get only immediate children (non-recursive)
            var items = new List<FileSystemItem>();

            try
            {
                // Get immediate subdirectories only
                DirectoryInfo[] subdirectories;
                try
                {
                    subdirectories = directoryInfo.GetDirectories();
                }
                catch (UnauthorizedAccessException)
                {
                    _logger.LogWarning("Access denied to directory: {Path}", directoryInfo.FullName);
                    // Continue to try files
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error getting subdirectories from: {Path}", directoryInfo.FullName);
                }

                // Get immediate files only
                FileInfo[] files;
                try
                {
                    files = directoryInfo.GetFiles();
                }
                catch (UnauthorizedAccessException)
                {
                    _logger.LogWarning("Access denied to files in directory: {Path}", directoryInfo.FullName);
                    files = Array.Empty<FileInfo>();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error getting files from: {Path}", directoryInfo.FullName);
                    files = Array.Empty<FileInfo>();
                }

                // Add directories
                try
                {
                    var dirs = directoryInfo.GetDirectories();
                    foreach (var dirInfo in dirs)
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
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error processing subdirectory: {Path}", dirInfo.FullName);
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    _logger.LogWarning("Access denied to directory: {Path}", directoryInfo.FullName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error getting subdirectories from: {Path}", directoryInfo.FullName);
                }

                // Add files
                foreach (var fileInfo in files)
                {
                    try
                    {
                        items.Add(new FileSystemItem
                        {
                            Name = fileInfo.Name,
                            Path = fileInfo.FullName,
                            Type = "file",
                            Size = fileInfo.Length,
                            LastModified = fileInfo.LastWriteTimeUtc,
                            Permissions = GetUnixPermissions(fileInfo.FullName),
                            Md5 = null // Don't calculate MD5 for browsing (too slow)
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error processing file: {Path}", fileInfo.FullName);
                    }
                }
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

            _logger.LogInformation("Browsed directory: {Path}, {DirCount} directories, {FileCount} files", 
                directoryPath, dirCount, fileCount);

            // Sort: directories first, then files, both alphabetically
            return Ok(items
                .OrderBy(i => i.Type == "file") // Directories first (false < true)
                .ThenBy(i => i.Name)
                .ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing directory browse request: {Path}", dir);
            return StatusCode(500, new { message = $"An error occurred: {ex.Message}" });
        }
    }

    private string? GetUnixPermissions(string path)
    {
        try
        {
            // On Unix-like systems, get file permissions using stat command
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

