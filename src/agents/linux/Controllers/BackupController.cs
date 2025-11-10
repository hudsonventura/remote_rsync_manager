using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using agent.Attributes;

namespace agent.Controllers;

/// <summary>
/// Controller for backup operations that require authentication
/// </summary>
[ApiController]
[Route("[controller]")]
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
    [HttpGet("status")]
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
    [HttpPost("execute")]
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
    [HttpGet("info")]
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
}

public record ExecuteBackupRequest(string Source, string Destination);

