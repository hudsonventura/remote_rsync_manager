using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using server.Data;
using server.Models;

namespace server.Controllers;

[ApiController]
[Authorize]
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

    [HttpGet("/api/backupplan/{id}/executions")]
    [ProducesResponseType(typeof(List<BackupExecutionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBackupPlanExecutions(Guid id)
    {
        try
        {
            // Verify backup plan exists
            var backupPlan = await _context.BackupPlans.FindAsync(id);
            if (backupPlan == null)
            {
                return NotFound(new { message = "Backup plan not found" });
            }

            var executions = await _logContext.BackupExecutions
                .Where(e => e.backupPlanId == id)
                .OrderByDescending(e => e.startDateTime)
                .Select(e => new BackupExecutionResponse
                {
                    Id = e.id,
                    Name = e.name,
                    StartDateTime = e.startDateTime,
                    EndDateTime = e.endDateTime
                })
                .ToListAsync();

            return Ok(executions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving executions for backup plan {BackupPlanId}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving executions", error = ex.Message });
        }
    }

    [HttpGet("/api/backupplan/{id}/logs")]
    [ProducesResponseType(typeof(List<LogEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBackupPlanLogs(
        Guid id,
        [FromQuery] Guid? executionId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? action = null,
        [FromQuery] string? fileName = null,
        [FromQuery] long? minSize = null,
        [FromQuery] long? maxSize = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? sortBy = "datetime",
        [FromQuery] string? sortOrder = "desc")
    {
        try
        {
            // Verify backup plan exists
            var backupPlan = await _context.BackupPlans.FindAsync(id);
            if (backupPlan == null)
            {
                return NotFound(new { message = "Backup plan not found" });
            }

            // Start with base query
            var query = _logContext.LogEntries.Where(log => log.backupPlanId == id);

            // Filter by executionId if provided
            if (executionId.HasValue)
            {
                query = query.Where(log => log.executionId == executionId.Value);
            }

            // Apply filters
            if (!string.IsNullOrWhiteSpace(action) && action != "All")
            {
                query = query.Where(log => log.action == action);
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                // Use ToLower() for case-insensitive search (SQLite compatible)
                var fileNameLower = fileName.ToLower();
                query = query.Where(log => log.fileName.ToLower().Contains(fileNameLower));
            }

            if (minSize.HasValue)
            {
                query = query.Where(log => log.size.HasValue && log.size >= minSize.Value);
            }

            if (maxSize.HasValue)
            {
                query = query.Where(log => log.size.HasValue && log.size <= maxSize.Value);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(log => log.datetime >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(log => log.datetime <= toDate.Value);
            }

            // Get total count after filters
            var totalCount = await query.CountAsync();

            // Apply sorting
            var sortByLower = sortBy?.ToLower() ?? "datetime";
            var sortOrderLower = sortOrder?.ToLower() ?? "desc";

            if (sortByLower == "filename")
            {
                query = sortOrderLower == "asc"
                    ? query.OrderBy(log => log.fileName)
                    : query.OrderByDescending(log => log.fileName);
            }
            else if (sortByLower == "size")
            {
                query = sortOrderLower == "asc"
                    ? query.OrderBy(log => log.size ?? 0)
                    : query.OrderByDescending(log => log.size ?? 0);
            }
            else if (sortByLower == "action")
            {
                query = sortOrderLower == "asc"
                    ? query.OrderBy(log => log.action)
                    : query.OrderByDescending(log => log.action);
            }
            else // datetime (default)
            {
                query = sortOrderLower == "asc"
                    ? query.OrderBy(log => log.datetime)
                    : query.OrderByDescending(log => log.datetime);
            }

            // Get paginated logs
            var logs = await query
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
            _logger.LogError(ex, "Error retrieving logs for backup plan {BackupPlanId}. Error:{error}", id, ex.Message);
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

    [HttpGet("/api/backupplan/{id}/executions/{executionId}/stats")]
    [ProducesResponseType(typeof(ExecutionStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExecutionStats(Guid id, Guid executionId)
    {
        try
        {
            // Verify backup plan exists
            var backupPlan = await _context.BackupPlans.FindAsync(id);
            if (backupPlan == null)
            {
                return NotFound(new { message = "Backup plan not found" });
            }

            // Get execution details
            var execution = await _logContext.BackupExecutions
                .FirstOrDefaultAsync(e => e.id == executionId && e.backupPlanId == id);

            if (execution == null)
            {
                return NotFound(new { message = "Execution not found" });
            }

            // Get all logs for this execution with action "Copy"
            var copyLogs = await _logContext.LogEntries
                .Where(log => log.executionId == executionId && log.action == "Copy")
                .ToListAsync();

            // Get ignored and deleted counts
            var ignoredCount = await _logContext.LogEntries
                .CountAsync(log => log.executionId == executionId && log.action == "Ignored");
            
            var deletedCount = await _logContext.LogEntries
                .CountAsync(log => log.executionId == executionId && log.action == "Delete");

            // Calculate total size (only for Copy actions)
            var totalSize = copyLogs.Sum(l => l.size ?? 0);
            var fileCount = copyLogs.Count;

            // Calculate duration in seconds and average speed
            double? durationSeconds = null;
            double? averageSpeedBytesPerSecond = null;

            // Try to get transfer speed from rsync output (saved at the end of execution)
            var transferSpeedLog = await _logContext.LogEntries
                .Where(log => log.executionId == executionId && 
                             log.fileName == "rsync-transfer-speed" &&
                             log.reason.StartsWith("TransferSpeed:"))
                .OrderByDescending(log => log.datetime)
                .FirstOrDefaultAsync();

            if (execution.endDateTime.HasValue)
            {
                // Backup finished - use actual duration
                var duration = execution.endDateTime.Value - execution.startDateTime;
                durationSeconds = duration.TotalSeconds;

                // Use transfer speed from rsync output if available, otherwise calculate
                if (transferSpeedLog != null)
                {
                    var speedValue = transferSpeedLog.reason.Replace("TransferSpeed:", "");
                    if (double.TryParse(speedValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var speed))
                    {
                        averageSpeedBytesPerSecond = speed;
                    }
                }
                
                // Fallback to calculated speed if not available from rsync
                if (!averageSpeedBytesPerSecond.HasValue && durationSeconds > 0)
                {
                    averageSpeedBytesPerSecond = totalSize / durationSeconds.Value;
                }
            }
            else
            {
                // Backup in progress - calculate real-time speed
                var currentDuration = DateTime.UtcNow - execution.startDateTime;
                durationSeconds = currentDuration.TotalSeconds;

                // Calculate current average speed based on elapsed time
                if (durationSeconds > 0 && totalSize > 0)
                {
                    averageSpeedBytesPerSecond = totalSize / durationSeconds.Value;
                }
            }

            // Determine status based on milestone logs and execution completion
            string status = "Unknown";
            if (execution.endDateTime.HasValue)
            {
                status = "Finished";
            }
            else
            {
                // Check latest milestone to determine current phase
                var latestMilestone = await _logContext.LogEntries
                    .Where(log => log.executionId == executionId && log.action == "Milestone")
                    .OrderByDescending(log => log.datetime)
                    .FirstOrDefaultAsync();

                if (latestMilestone != null)
                {
                    if (latestMilestone.reason.Contains("SourceAnalysisStarted"))
                    {
                        status = "Analyzing";
                    }
                    else if (latestMilestone.reason.Contains("CopiesStarted"))
                    {
                        status = "Copying";
                    }
                    else if (latestMilestone.reason.Contains("CopiesFinished"))
                    {
                        status = "Finalizing";
                    }
                }
                else
                {
                    status = "Starting";
                }
            }

            var stats = new ExecutionStatsResponse
            {
                ExecutionId = executionId,
                StartDateTime = execution.startDateTime,
                EndDateTime = execution.endDateTime,
                TotalSize = totalSize,
                FileCount = fileCount,
                IgnoredCount = ignoredCount,
                DeletedCount = deletedCount,
                DurationSeconds = durationSeconds,
                AverageSpeedBytesPerSecond = averageSpeedBytesPerSecond,
                Status = status,
                CurrentFileName = execution.currentFileName,
                CurrentFilePath = execution.currentFilePath
            };

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving execution stats for backup plan {BackupPlanId}, execution {ExecutionId}", id, executionId);
            return StatusCode(500, new
            {
                message = "An error occurred while retrieving execution stats",
                error = ex.Message
            });
        }
    }

    [HttpGet("/api/logs")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllLogs(
        [FromQuery] Guid? executionId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? action = null,
        [FromQuery] string? fileName = null,
        [FromQuery] long? minSize = null,
        [FromQuery] long? maxSize = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] Guid? backupPlanId = null,
        [FromQuery] string? sortBy = "datetime",
        [FromQuery] string? sortOrder = "desc")
    {
        try
        {
            // Start with base query - all logs
            var query = _logContext.LogEntries.AsQueryable();

            // Filter by executionId if provided (highest priority)
            if (executionId.HasValue)
            {
                query = query.Where(log => log.executionId == executionId.Value);
            }

            // Filter by backupPlanId if provided
            if (backupPlanId.HasValue)
            {
                query = query.Where(log => log.backupPlanId == backupPlanId.Value);
            }

            // Apply filters
            if (!string.IsNullOrWhiteSpace(action) && action != "All")
            {
                query = query.Where(log => log.action == action);
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                // Use ToLower() for case-insensitive search (SQLite compatible)
                var fileNameLower = fileName.ToLower();
                query = query.Where(log => log.fileName.ToLower().Contains(fileNameLower));
            }

            if (minSize.HasValue)
            {
                query = query.Where(log => log.size.HasValue && log.size >= minSize.Value);
            }

            if (maxSize.HasValue)
            {
                query = query.Where(log => log.size.HasValue && log.size <= maxSize.Value);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(log => log.datetime >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(log => log.datetime <= toDate.Value);
            }

            // Get total count after filters
            var totalCount = await query.CountAsync();

            // Apply sorting
            var sortByLower = sortBy?.ToLower() ?? "datetime";
            var sortOrderLower = sortOrder?.ToLower() ?? "desc";

            if (sortByLower == "filename")
            {
                query = sortOrderLower == "asc"
                    ? query.OrderBy(log => log.fileName)
                    : query.OrderByDescending(log => log.fileName);
            }
            else if (sortByLower == "size")
            {
                query = sortOrderLower == "asc"
                    ? query.OrderBy(log => log.size ?? 0)
                    : query.OrderByDescending(log => log.size ?? 0);
            }
            else if (sortByLower == "action")
            {
                query = sortOrderLower == "asc"
                    ? query.OrderBy(log => log.action)
                    : query.OrderByDescending(log => log.action);
            }
            else // datetime (default)
            {
                query = sortOrderLower == "asc"
                    ? query.OrderBy(log => log.datetime)
                    : query.OrderByDescending(log => log.datetime);
            }

            // Get paginated logs with backup plan information
            var logs = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(log => new
                {
                    log.id,
                    log.datetime,
                    log.fileName,
                    log.filePath,
                    log.size,
                    log.action,
                    log.reason,
                    log.backupPlanId
                })
                .ToListAsync();

            // Get backup plan names for the logs
            var backupPlanIds = logs.Select(l => l.backupPlanId).Distinct().ToList();
            var backupPlans = await _context.BackupPlans
                .Where(bp => backupPlanIds.Contains(bp.id))
                .Select(bp => new { bp.id, bp.name })
                .ToListAsync();

            var backupPlanDict = backupPlans.ToDictionary(bp => bp.id, bp => bp.name);

            // Map logs with backup plan names
            var logResponses = logs.Select(log => new AllLogsEntryResponse
            {
                Id = log.id,
                DateTime = log.datetime,
                FileName = log.fileName,
                FilePath = log.filePath,
                Size = log.size,
                Action = log.action,
                Reason = log.reason,
                BackupPlanId = log.backupPlanId,
                BackupPlanName = backupPlanDict.ContainsKey(log.backupPlanId) 
                    ? backupPlanDict[log.backupPlanId] 
                    : "Unknown Plan"
            }).ToList();

            return Ok(new
            {
                logs = logResponses,
                totalCount = totalCount,
                page = page,
                pageSize = pageSize,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all logs. Error:{error}", ex.Message);
            return StatusCode(500, new { message = "An error occurred while retrieving logs", error = ex.Message });
        }
    }

    [HttpGet("/api/executions")]
    [ProducesResponseType(typeof(List<AllExecutionsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllExecutions()
    {
        try
        {
            // Get all executions with backup plan information
            var executions = await _logContext.BackupExecutions
                .OrderByDescending(e => e.startDateTime)
                .Select(e => new
                {
                    e.id,
                    e.backupPlanId,
                    e.name,
                    e.startDateTime,
                    e.endDateTime
                })
                .ToListAsync();

            // Get backup plan names for the executions
            var backupPlanIds = executions.Select(e => e.backupPlanId).Distinct().ToList();
            var backupPlans = await _context.BackupPlans
                .Where(bp => backupPlanIds.Contains(bp.id))
                .Select(bp => new { bp.id, bp.name })
                .ToListAsync();

            var backupPlanDict = backupPlans.ToDictionary(bp => bp.id, bp => bp.name);

            // Map executions with backup plan names
            var executionResponses = executions.Select(e => new AllExecutionsResponse
            {
                Id = e.id,
                BackupPlanId = e.backupPlanId,
                BackupPlanName = backupPlanDict.ContainsKey(e.backupPlanId)
                    ? backupPlanDict[e.backupPlanId]
                    : "Unknown Plan",
                Name = e.name,
                StartDateTime = e.startDateTime,
                EndDateTime = e.endDateTime
            }).ToList();

            return Ok(executionResponses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all executions");
            return StatusCode(500, new { message = "An error occurred while retrieving executions", error = ex.Message });
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

public class BackupExecutionResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDateTime { get; set; }
    public DateTime? EndDateTime { get; set; }
}

public class ExecutionStatsResponse
{
    public Guid ExecutionId { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime? EndDateTime { get; set; }
    public long TotalSize { get; set; }
    public int FileCount { get; set; }
    public int IgnoredCount { get; set; }
    public int DeletedCount { get; set; }
    public double? DurationSeconds { get; set; }
    public double? AverageSpeedBytesPerSecond { get; set; }
    public string Status { get; set; } = "Unknown";
    public string? CurrentFileName { get; set; }
    public string? CurrentFilePath { get; set; }
}

public class AllLogsEntryResponse
{
    public Guid Id { get; set; }
    public DateTime DateTime { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long? Size { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid BackupPlanId { get; set; }
    public string BackupPlanName { get; set; } = string.Empty;
}

public class AllExecutionsResponse
{
    public Guid Id { get; set; }
    public Guid BackupPlanId { get; set; }
    public string BackupPlanName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime StartDateTime { get; set; }
    public DateTime? EndDateTime { get; set; }
}


