using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using server.Data;
using server.Models;

namespace server.Controllers;

[ApiController]
public class BackupLogController : ControllerBase
{
    private readonly LogDbContext _logContext;
    private readonly DBContext _context;
    private readonly ILogger<BackupLogController> _logger;

    public BackupLogController(LogDbContext logContext, DBContext context, ILogger<BackupLogController> logger)
    {
        _logContext = logContext;
        _context = context;
        _logger = logger;
    }

    [HttpGet("/api/backupplan/{id}/logs")]
    [ProducesResponseType(typeof(List<LogEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBackupPlanLogs(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        try
        {
            // Verify backup plan exists
            var backupPlan = await _context.BackupPlans.FindAsync(id);
            if (backupPlan == null)
            {
                return NotFound(new { message = "Backup plan not found" });
            }

            // Get total count
            var totalCount = await _logContext.LogEntries
                .Where(log => log.backupPlanId == id)
                .CountAsync();

            // Get paginated logs, ordered by datetime descending (newest first)
            var logs = await _logContext.LogEntries
                .Where(log => log.backupPlanId == id)
                .OrderByDescending(log => log.datetime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(log => new LogEntryResponse
                {
                    Id = log.id,
                    DateTime = log.datetime,
                    FileName = log.fileName,
                    FilePath = log.filePath,
                    Size = log.size,
                    Action = log.action,
                    Reason = log.reason
                })
                .ToListAsync();

            return Ok(new
            {
                logs = logs,
                totalCount = totalCount,
                page = page,
                pageSize = pageSize,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving logs for backup plan {BackupPlanId}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving logs", error = ex.Message });
        }
    }

    [HttpGet("/api/backupplan/{id}/logs/summary")]
    [ProducesResponseType(typeof(LogSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBackupPlanLogSummary(Guid id)
    {
        try
        {
            // Verify backup plan exists
            var backupPlan = await _context.BackupPlans.FindAsync(id);
            if (backupPlan == null)
            {
                return NotFound(new { message = "Backup plan not found" });
            }

            var logs = await _logContext.LogEntries
                .Where(log => log.backupPlanId == id)
                .ToListAsync();

            var summary = new LogSummaryResponse
            {
                TotalFiles = logs.Count,
                Copied = logs.Count(l => l.action == "Copy"),
                Deleted = logs.Count(l => l.action == "Delete"),
                Ignored = logs.Count(l => l.action == "Ignored"),
                LastExecution = logs.OrderByDescending(l => l.datetime).FirstOrDefault()?.datetime
            };

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving log summary for backup plan {BackupPlanId}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving log summary", error = ex.Message });
        }
    }
}

public class LogEntryResponse
{
    public Guid Id { get; set; }
    public DateTime DateTime { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long? Size { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class LogSummaryResponse
{
    public int TotalFiles { get; set; }
    public int Copied { get; set; }
    public int Deleted { get; set; }
    public int Ignored { get; set; }
    public DateTime? LastExecution { get; set; }
}

