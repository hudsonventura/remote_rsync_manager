using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using server.Models;

namespace server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FileSystemController : ControllerBase
{
    private readonly ILogger<FileSystemController> _logger;

    public FileSystemController(ILogger<FileSystemController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Browse the local file system (non-recursive directory listing)
    /// </summary>
    /// <param name="dir">Directory path to browse (query parameter). Defaults to "/" on Unix or "C:\" on Windows if not provided.</param>
    /// <returns>List of immediate files and directories in the specified path</returns>
    [HttpGet("browse")]
    [ProducesResponseType(typeof(List<FileSystemItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult BrowseDirectory([FromQuery] string? dir)
    {
        var allowedRoots = GetAllowedRoots();

        // Determine default path: if dir is not provided or asks for root, point to first available allowed root
        string directoryPath;
        if (string.IsNullOrWhiteSpace(dir) || dir == "/" || dir == "C:\\" || dir == "\\")
        {
            directoryPath = allowedRoots.FirstOrDefault(Directory.Exists) ?? allowedRoots[0];
            if (!Directory.Exists(directoryPath))
            {
                try { Directory.CreateDirectory(directoryPath); } catch { }
            }
        }
        else
        {
            directoryPath = dir.Trim();
        }

        try
        {
            // Security check: prevent directory traversal attacks
            if (directoryPath.Contains(".."))
            {
                _logger.LogWarning("Potentially unsafe directory path requested: {Path}", directoryPath);
                return BadRequest(new { message = "Invalid directory path: directory traversal (..) is not allowed" });
            }

            var fullPath = Path.GetFullPath(directoryPath);

            // Scope check: must be inside one of the allowed storage roots
            bool isAllowed = allowedRoots.Any(root => 
                fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) || 
                fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

            if (!isAllowed)
            {
                _logger.LogWarning("Access denied to directory outside allowed roots: {Path}", fullPath);
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Access denied: browsing is restricted to storage paths (/store, /host, data)." });
            }

            // Check if directory exists
            if (!Directory.Exists(fullPath))
            {
                _logger.LogWarning("Directory not found: {Path}", fullPath);
                return NotFound(new { message = $"Directory not found: {fullPath}" });
            }

            directoryPath = fullPath;

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
                                PathName = dirInfo.FullName,
                                Path = directoryPath, // Set the current directory path
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
                            PathName = fileInfo.FullName,
                            Path = fileInfo.DirectoryName ?? directoryPath, // Use DirectoryName or fallback to directoryPath
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

    /// <summary>
    /// Create a new directory within allowed storage paths
    /// </summary>
    [HttpPost("create-directory")]
    [HttpPost("directory")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult CreateDirectory([FromBody] CreateDirectoryRequest request)
    {
        if (request == null)
        {
            return BadRequest(new { message = "Request body is required" });
        }

        string targetPath;
        if (!string.IsNullOrWhiteSpace(request.Path))
        {
            targetPath = request.Path.Trim();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.DirectoryName))
            {
                return BadRequest(new { message = "Directory name is required" });
            }

            var dirName = request.DirectoryName.Trim();
            if (dirName.Contains('/') || dirName.Contains('\\') || dirName.Contains(".."))
            {
                return BadRequest(new { message = "Directory name contains invalid path traversal characters" });
            }

            if (dirName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return BadRequest(new { message = "Directory name contains invalid characters" });
            }

            var allowedRoots = GetAllowedRoots();
            string parentDirectory;
            if (string.IsNullOrWhiteSpace(request.ParentPath) || request.ParentPath == "/" || request.ParentPath == "C:\\" || request.ParentPath == "\\")
            {
                parentDirectory = allowedRoots.FirstOrDefault(Directory.Exists) ?? allowedRoots[0];
            }
            else
            {
                parentDirectory = request.ParentPath.Trim();
            }

            targetPath = Path.Combine(parentDirectory, dirName);
        }

        try
        {
            if (targetPath.Contains(".."))
            {
                _logger.LogWarning("Potentially unsafe directory creation requested: {Path}", targetPath);
                return BadRequest(new { message = "Invalid directory path: directory traversal (..) is not allowed" });
            }

            var fullPath = Path.GetFullPath(targetPath);
            var allowedRoots = GetAllowedRoots();

            // Scope check: must be inside one of the allowed storage roots
            bool isAllowed = allowedRoots.Any(root =>
                fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

            if (!isAllowed)
            {
                _logger.LogWarning("Access denied creating directory outside allowed roots: {Path}", fullPath);
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Access denied: creating directories is restricted to storage paths (/store, /host, data)." });
            }

            if (Directory.Exists(fullPath))
            {
                return Conflict(new { message = $"Directory already exists: {fullPath}" });
            }

            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            {
                return BadRequest(new { message = $"Parent directory does not exist: {parent}" });
            }

            Directory.CreateDirectory(fullPath);
            _logger.LogInformation("Directory created successfully: {Path}", fullPath);

            var dirInfo = new DirectoryInfo(fullPath);
            return Ok(new
            {
                success = true,
                message = "Directory created successfully",
                name = dirInfo.Name,
                pathName = dirInfo.FullName,
                type = "directory",
                lastModified = dirInfo.LastWriteTimeUtc.ToString("o")
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Permission denied creating directory: {Path}", targetPath);
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Permission denied: unable to create directory." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating directory: {Path}", targetPath);
            return StatusCode(500, new { message = $"An error occurred while creating directory: {ex.Message}" });
        }
    }

    private List<string> GetAllowedRoots()
    {
        var roots = new List<string>();
        var dataDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "data"));
        roots.Add(dataDirectory);

        var storeEnv = Environment.GetEnvironmentVariable("STORE_PATH") ?? "/store";
        try
        {
            if (Directory.Exists(storeEnv)) roots.Add(Path.GetFullPath(storeEnv));
            else if (Directory.Exists("/store")) roots.Add("/store");
        }
        catch { }

        var hostEnv = Environment.GetEnvironmentVariable("HOST_PATH") ?? "/host";
        try
        {
            if (Directory.Exists(hostEnv)) roots.Add(Path.GetFullPath(hostEnv));
            else if (Directory.Exists("/host")) roots.Add("/host");
        }
        catch { }

        var localStore = Path.Combine(dataDirectory, "store");
        if (!roots.Contains(localStore)) roots.Add(localStore);
        var localHost = Path.Combine(dataDirectory, "host");
        if (!roots.Contains(localHost)) roots.Add(localHost);

        return roots;
    }

    private string? GetUnixPermissions(string path)
    {
        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                var mode = System.IO.File.GetUnixFileMode(path);
                int octal = ((int)mode) & 0x1FF;
                return Convert.ToString(octal, 8).PadLeft(3, '0');
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting Unix permissions for: {Path}", path);
        }
        return null;
    }
}

