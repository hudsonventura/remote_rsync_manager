using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using server.Data;
using server.Models;

namespace server.Controllers;

[ApiController]
public class SettingsController : ControllerBase
{
    private readonly DBContext _context;
    private readonly LogDbContext _logContext;
    private readonly ILogger<SettingsController> _logger;
    private readonly IWebHostEnvironment _environment;

    public SettingsController(
        DBContext context, 
        LogDbContext logContext,
        ILogger<SettingsController> logger,
        IWebHostEnvironment environment)
    {
        _context = context;
        _logContext = logContext;
        _logger = logger;
        _environment = environment;
    }

    private string ResolveDbPath(string connectionString)
    {
        if (connectionString.StartsWith("Data Source="))
        {
            var dbPath = connectionString.Substring("Data Source=".Length);
            // If path contains "data/", resolve it to the data directory
            var dataDirectory = Path.Combine(_environment.ContentRootPath, "data");
            if (!Directory.Exists(dataDirectory))
            {
                dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "data");
            }

            if (dbPath.StartsWith("data/") || dbPath.StartsWith("data\\"))
            {
                var fileName = Path.GetFileName(dbPath);
                dbPath = Path.Combine(dataDirectory, fileName);
            }
            // If it's a relative path, make it relative to data directory
            else if (!Path.IsPathRooted(dbPath))
            {
                dbPath = Path.Combine(dataDirectory, dbPath);
            }
            return dbPath;
        }
        return connectionString;
    }

    private string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 B";
        
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size = size / 1024;
        }
        
        return $"{size:0.##} {sizes[order]}";
    }

    [HttpGet("/api/settings/log-retention-date")]
    [ProducesResponseType(typeof(LogRetentionDateResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLogRetentionDate()
    {
        try
        {
            var setting = await _context.AppSettings
                .FirstOrDefaultAsync(s => s.key == "LogRetentionDate");

            var response = new LogRetentionDateResponse
            {
                Date = setting != null && DateTime.TryParse(setting.value, out var date) ? date : null
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving log retention date");
            return StatusCode(500, new { message = "An error occurred while retrieving log retention date", error = ex.Message });
        }
    }

    [HttpPost("/api/settings/log-retention-date")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetLogRetentionDate([FromBody] LogRetentionDateRequest request)
    {
        try
        {
            var setting = await _context.AppSettings
                .FirstOrDefaultAsync(s => s.key == "LogRetentionDate");

            if (setting == null)
            {
                setting = new AppSettings
                {
                    id = Guid.NewGuid(),
                    key = "LogRetentionDate",
                    value = request.Date?.ToString("yyyy-MM-dd") ?? string.Empty,
                    created_at = DateTime.UtcNow,
                    updated_at = DateTime.UtcNow
                };
                _context.AppSettings.Add(setting);
            }
            else
            {
                setting.value = request.Date?.ToString("yyyy-MM-dd") ?? string.Empty;
                setting.updated_at = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Log retention date saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving log retention date");
            return StatusCode(500, new { message = "An error occurred while saving log retention date", error = ex.Message });
        }
    }

    [HttpGet("/api/settings/log-retention-period")]
    [ProducesResponseType(typeof(LogRetentionPeriodResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLogRetentionPeriod()
    {
        try
        {
            var setting = await _context.AppSettings
                .FirstOrDefaultAsync(s => s.key == "LogRetentionPeriodMonths");

            var response = new LogRetentionPeriodResponse
            {
                Months = setting != null && int.TryParse(setting.value, out var months) ? months : null
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving log retention period");
            return StatusCode(500, new { message = "An error occurred while retrieving log retention period", error = ex.Message });
        }
    }

    [HttpPost("/api/settings/log-retention-period")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetLogRetentionPeriod([FromBody] LogRetentionPeriodRequest request)
    {
        try
        {
            if (request.Months.HasValue && request.Months.Value < 0)
            {
                return BadRequest(new { message = "Retention period must be 0 or greater" });
            }

            var setting = await _context.AppSettings
                .FirstOrDefaultAsync(s => s.key == "LogRetentionPeriodMonths");

            if (setting == null)
            {
                setting = new AppSettings
                {
                    id = Guid.NewGuid(),
                    key = "LogRetentionPeriodMonths",
                    value = request.Months?.ToString() ?? string.Empty,
                    created_at = DateTime.UtcNow,
                    updated_at = DateTime.UtcNow
                };
                _context.AppSettings.Add(setting);
            }
            else
            {
                setting.value = request.Months?.ToString() ?? string.Empty;
                setting.updated_at = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Log retention period saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving log retention period");
            return StatusCode(500, new { message = "An error occurred while saving log retention period", error = ex.Message });
        }
    }

    [HttpPost("/api/settings/delete-logs-before-date")]
    [ProducesResponseType(typeof(DeleteLogsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteLogsBeforeDate([FromBody] DeleteLogsRequest request)
    {
        try
        {
            if (!request.BeforeDate.HasValue)
            {
                return BadRequest(new { message = "BeforeDate is required" });
            }

            var beforeDate = request.BeforeDate.Value.Date;

            // Get executions that started before the date
            var executionsToDelete = await _logContext.BackupExecutions
                .Where(e => e.startDateTime < beforeDate)
                .Select(e => e.id)
                .ToListAsync();

            // Delete log entries for those executions
            var logsDeleted = await _logContext.LogEntries
                .Where(log => executionsToDelete.Contains(log.executionId))
                .CountAsync();

            _logContext.LogEntries.RemoveRange(
                _logContext.LogEntries.Where(log => executionsToDelete.Contains(log.executionId))
            );

            // Delete executions
            _logContext.BackupExecutions.RemoveRange(
                _logContext.BackupExecutions.Where(e => executionsToDelete.Contains(e.id))
            );

            await _logContext.SaveChangesAsync();

            // Delete notifications for deleted executions
            var notificationsDeleted = await _context.Notifications
                .Where(n => n.executionId.HasValue && executionsToDelete.Contains(n.executionId.Value))
                .CountAsync();

            _context.Notifications.RemoveRange(
                _context.Notifications.Where(n => n.executionId.HasValue && executionsToDelete.Contains(n.executionId.Value))
            );

            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted {NotificationsCount} notifications associated with deleted executions", notificationsDeleted);

            // Get database file path and perform VACUUM to free disk space
            var logsConnectionString = "Data Source=data/logs.db";
            var dbPath = ResolveDbPath(logsConnectionString);

            long spaceSaved = 0;

            // Perform VACUUM to reclaim disk space
            if (!string.IsNullOrEmpty(dbPath) && System.IO.File.Exists(dbPath))
            {
                try
                {
                    // Get file size before VACUUM
                    var fileInfoBefore = new System.IO.FileInfo(dbPath);
                    long sizeBefore = fileInfoBefore.Length;

                    // Close the current connection
                    await _logContext.Database.CloseConnectionAsync();

                    // Execute VACUUM using raw SQL
                    using (var connection = new SqliteConnection($"Data Source={dbPath}"))
                    {
                        await connection.OpenAsync();
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "VACUUM";
                            await command.ExecuteNonQueryAsync();
                        }
                    }

                    // Get file size after VACUUM
                    var fileInfoAfter = new System.IO.FileInfo(dbPath);
                    long sizeAfter = fileInfoAfter.Length;

                    spaceSaved = sizeBefore - sizeAfter;

                    _logger.LogInformation("VACUUM completed successfully on {DbPath}. Space saved: {SpaceSaved} bytes", dbPath, spaceSaved);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to execute VACUUM on {DbPath}", dbPath);
                    // Continue even if VACUUM fails
                }
            }

            // Format space saved for message
            string spaceSavedFormatted = FormatBytes(spaceSaved);
            
            var response = new DeleteLogsResponse
            {
                ExecutionsDeleted = executionsToDelete.Count,
                LogsDeleted = logsDeleted,
                NotificationsDeleted = notificationsDeleted,
                SpaceSavedBytes = spaceSaved,
                Message = $"Successfully deleted {executionsToDelete.Count} executions, {logsDeleted} log entries, and {notificationsDeleted} notifications. Disk space saved: {spaceSavedFormatted}"
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting logs before date");
            return StatusCode(500, new { message = "An error occurred while deleting logs", error = ex.Message });
        }
    }
}

public class LogRetentionDateRequest
{
    public DateTime? Date { get; set; }
}

public class LogRetentionDateResponse
{
    public DateTime? Date { get; set; }
}

public class LogRetentionPeriodRequest
{
    public int? Months { get; set; }
}

public class LogRetentionPeriodResponse
{
    public int? Months { get; set; }
}

public class DeleteLogsRequest
{
    public DateTime? BeforeDate { get; set; }
}

public class DeleteLogsResponse
{
    public int ExecutionsDeleted { get; set; }
    public int LogsDeleted { get; set; }
    public int NotificationsDeleted { get; set; }
    public long SpaceSavedBytes { get; set; }
    public string Message { get; set; } = string.Empty;
}

