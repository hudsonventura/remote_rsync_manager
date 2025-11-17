using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using AgentCommon.Attributes;

namespace AgentCommon.Controllers;

/// <summary>
/// Controller for backup operations that require authentication
/// </summary>
[ApiController]
[RequireAgentToken]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public class BackupController : ControllerBase
{
    private readonly ILogger<BackupController> _logger;

    public BackupController(ILogger<BackupController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get backup status - requires authentication
    /// </summary>
    [HttpGet("/Backup/status")]
    public IActionResult GetBackupStatus()
    {
        _logger.LogInformation("Backup status requested by authenticated server");
        
        return Ok(new
        {
            status = "ready",
            message = "Agent is ready to perform backups",
            timestamp = DateTime.UtcNow,
            version = "1.0.0"
        });
    }

    /// <summary>
    /// Execute a backup plan - requires authentication
    /// </summary>
    [HttpPost("/Backup/execute")]
    public IActionResult ExecuteBackup([FromBody] ExecuteBackupRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Source) || string.IsNullOrWhiteSpace(request.Destination))
        {
            return BadRequest(new { message = "Source and destination are required" });
        }

        _logger.LogInformation("Backup execution requested: Source={Source}, Destination={Destination}", 
            request.Source, request.Destination);

        // TODO: Implement actual backup logic here
        // For now, just return a success response
        
        return Ok(new
        {
            message = "Backup execution started",
            source = request.Source,
            destination = request.Destination,
            startedAt = DateTime.UtcNow,
            status = "in_progress"
        });
    }

    /// <summary>
    /// Get agent information - requires authentication
    /// </summary>
    [HttpGet("/Backup/info")]
    public IActionResult GetAgentInfo()
    {
        _logger.LogInformation("Agent info requested by authenticated server");

        return Ok(new
        {
            agentId = Environment.MachineName,
            hostname = Environment.MachineName,
            platform = Environment.OSVersion.Platform.ToString(),
            version = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Download a file from the agent - requires authentication
    /// </summary>
    /// <param name="filePath">Path to the file to download (query parameter)</param>
    /// <returns>File content as stream</returns>
    [HttpGet("/Download")]
    public IActionResult DownloadFile([FromQuery] string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return BadRequest(new { message = "File path is required. Use ?filePath=/path/to/file" });
        }

        try
        {
            var normalizedPath = filePath.Trim();

            // Security check: prevent directory traversal attacks
            if (normalizedPath.Contains(".."))
            {
                _logger.LogWarning("Potentially unsafe file path requested: {Path}", normalizedPath);
                return BadRequest(new { message = "Invalid file path: directory traversal (..) is not allowed" });
            }

            // Check if file exists
            if (!System.IO.File.Exists(normalizedPath))
            {
                _logger.LogWarning("File not found: {Path}", normalizedPath);
                return NotFound(new { message = $"File not found: {normalizedPath}" });
            }

            // Check if it's actually a file (not a directory)
            var fileInfo = new FileInfo(normalizedPath);
            if (!fileInfo.Exists)
            {
                _logger.LogWarning("Path is not a file: {Path}", normalizedPath);
                return BadRequest(new { message = $"Path is not a file: {normalizedPath}" });
            }

            _logger.LogInformation("Downloading file: {Path}, Size: {Size} bytes", normalizedPath, fileInfo.Length);

            // Return file as stream
            var fileStream = System.IO.File.OpenRead(normalizedPath);
            return File(fileStream, "application/octet-stream", fileInfo.Name);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied to file: {Path}", filePath);
            return StatusCode(403, new { message = $"Access denied to file: {filePath}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file: {Path}", filePath);
            return StatusCode(500, new { message = $"Error downloading file: {ex.Message}" });
        }
    }
}

public record ExecuteBackupRequest(string Source, string Destination);

