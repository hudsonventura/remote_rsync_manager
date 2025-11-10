using Microsoft.AspNetCore.Mvc;
using agent.Attributes;

namespace agent.Controllers;

/// <summary>
/// Controller for listing files and directories
/// </summary>
[ApiController]
[Route("[controller]")]
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
    [HttpGet]
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
            
            // Get files and directories
            var files = new List<FileSystemItem>();
            var directories = new List<FileSystemItem>();

            try
            {
                // Get directories
                foreach (var dirInfo in directoryInfo.GetDirectories())
                {
                    directories.Add(new FileSystemItem
                    {
                        Name = dirInfo.Name,
                        Path = dirInfo.FullName,
                        Type = "directory",
                        Size = null,
                        LastModified = dirInfo.LastWriteTimeUtc,
                        Permissions = GetUnixPermissions(dirInfo.FullName)
                    });
                }

                // Get files
                foreach (var fileInfo in directoryInfo.GetFiles())
                {
                    files.Add(new FileSystemItem
                    {
                        Name = fileInfo.Name,
                        Path = fileInfo.FullName,
                        Type = "file",
                        Size = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTimeUtc,
                        Permissions = GetUnixPermissions(fileInfo.FullName)
                    });
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

            _logger.LogInformation("Listed directory: {Path}, {DirCount} directories, {FileCount} files", 
                directoryPath, directories.Count, files.Count);

            return Ok(new
            {
                path = directoryPath,
                parent = directoryInfo.Parent?.FullName,
                directories = directories.OrderBy(d => d.Name),
                files = files.OrderBy(f => f.Name),
                totalDirectories = directories.Count,
                totalFiles = files.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing directory listing request: {Path}", dir);
            return StatusCode(500, new { message = $"An error occurred: {ex.Message}" });
        }
    }

    private string? GetUnixPermissions(string path)
    {
        try
        {
            // On Unix-like systems, try to get file permissions
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Exists || Directory.Exists(path))
                {
                    // Use stat command or Mono.Unix if available
                    // For now, return null as getting permissions requires platform-specific code
                    return null;
                }
            }
        }
        catch
        {
            // Ignore errors getting permissions
        }
        return null;
    }
}

/// <summary>
/// Represents a file or directory item
/// </summary>
public class FileSystemItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "file" or "directory"
    public long? Size { get; set; } // null for directories
    public DateTime LastModified { get; set; }
    public string? Permissions { get; set; }
}

