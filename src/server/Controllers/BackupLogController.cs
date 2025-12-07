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

            // Get rsync command from log entry
            var commandLog = await _logContext.LogEntries
                .Where(log => log.executionId == executionId && log.fileName == "rsync-command")
                .FirstOrDefaultAsync();
            var rsyncCommand = commandLog?.filePath ?? string.Empty;

            // Get rsync statistics from log entry
            var statsLog = await _logContext.LogEntries
                .Where(log => log.executionId == executionId && log.fileName == "rsync-stats")
                .OrderByDescending(log => log.datetime)
                .FirstOrDefaultAsync();

            // Initialize default values
            var stats = new ExecutionStatsResponse
            {
                ExecutionId = executionId,
                StartDateTime = execution.startDateTime,
                EndDateTime = execution.endDateTime,
                Status = "Unknown",
                CurrentFileName = execution.currentFileName,
                CurrentFilePath = execution.currentFilePath,
                RsyncCommand = rsyncCommand,
                TotalFilesToProcess = execution.totalFilesToProcess,
                CurrentFileIndex = execution.currentFileIndex
            };

            // Parse statistics from log entry if available
            if (statsLog != null)
            {
                var parts = statsLog.reason.Split('|');
                foreach (var part in parts)
                {
                    var keyValue = part.Split(':');
                    if (keyValue.Length == 2)
                    {
                        var key = keyValue[0];
                        var value = keyValue[1];
                        
                        switch (key)
                        {
                            case "TotalFiles":
                                if (int.TryParse(value.Replace(".", ""), out var totalFiles))
                                    stats.TotalFiles = totalFiles;
                                break;
                            case "RegularFiles":
                                if (int.TryParse(value.Replace(".", ""), out var regularFiles))
                                    stats.RegularFiles = regularFiles;
                                break;
                            case "Directories":
                                if (int.TryParse(value.Replace(".", ""), out var directories))
                                    stats.Directories = directories;
                                break;
                            case "CreatedFiles":
                                if (int.TryParse(value.Replace(".", ""), out var createdFiles))
                                    stats.CreatedFiles = createdFiles;
                                break;
                            case "DeletedFiles":
                                if (int.TryParse(value.Replace(".", ""), out var deletedFiles))
                                    stats.DeletedFiles = deletedFiles;
                                break;
                            case "TransferredFiles":
                                if (int.TryParse(value.Replace(".", ""), out var transferredFiles))
                                    stats.TransferredFiles = transferredFiles;
                                break;
                            case "TotalFileSize":
                                if (long.TryParse(value.Replace(".", ""), out var totalFileSize))
                                    stats.TotalFileSize = totalFileSize;
                                break;
                            case "TotalTransferredSize":
                                if (long.TryParse(value.Replace(".", ""), out var totalTransferredSize))
                                    stats.TotalTransferredSize = totalTransferredSize;
                                break;
                            case "LiteralData":
                                if (long.TryParse(value.Replace(".", ""), out var literalData))
                                    stats.LiteralData = literalData;
                                break;
                            case "MatchedData":
                                if (long.TryParse(value.Replace(".", ""), out var matchedData))
                                    stats.MatchedData = matchedData;
                                break;
                            case "FileListSize":
                                if (long.TryParse(value.Replace(".", ""), out var fileListSize))
                                    stats.FileListSize = fileListSize;
                                break;
                            case "FileListGenerationTime":
                                if (double.TryParse(value.Replace(",", "."), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var genTime))
                                    stats.FileListGenerationTime = genTime;
                                break;
                            case "FileListTransferTime":
                                if (double.TryParse(value.Replace(",", "."), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var transferTime))
                                    stats.FileListTransferTime = transferTime;
                                break;
                            case "TotalBytesSent":
                                if (long.TryParse(value.Replace(".", ""), out var bytesSent))
                                    stats.TotalBytesSent = bytesSent;
                                break;
                            case "TotalBytesReceived":
                                if (long.TryParse(value.Replace(".", ""), out var bytesReceived))
                                    stats.TotalBytesReceived = bytesReceived;
                                break;
                            case "TransferSpeed":
                                if (double.TryParse(value.Replace(",", "."), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var speed))
                                    stats.TransferSpeedBytesPerSecond = speed;
                                break;
                            case "Speedup":
                                if (double.TryParse(value.Replace(",", "."), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var speedup))
                                    stats.Speedup = speedup;
                                break;
                        }
                    }
                }
            }

            // Calculate duration
            if (execution.endDateTime.HasValue)
            {
                var duration = execution.endDateTime.Value - execution.startDateTime;
                stats.DurationSeconds = duration.TotalSeconds;
            }
            else
            {
                var currentDuration = DateTime.UtcNow - execution.startDateTime;
                stats.DurationSeconds = currentDuration.TotalSeconds;
            }

            // Determine status based on execution completion and finish log
            if (execution.endDateTime.HasValue)
            {
                // Execution has finished - check if it completed successfully or was interrupted
                var finishLog = await _logContext.LogEntries
                    .Where(log => log.executionId == executionId && log.fileName == "rsync-finish")
                    .OrderByDescending(log => log.datetime)
                    .FirstOrDefaultAsync();

                if (finishLog != null)
                {
                    // Check the action to determine if it was successful or failed
                    if (finishLog.action == "CopyError")
                    {
                        stats.Status = "Interrupted";
                    }
                    else if (finishLog.reason.Contains("finished successfully") || 
                             finishLog.reason.Contains("partial transfer"))
                    {
                        stats.Status = "Completed";
                    }
                    else
                    {
                        stats.Status = "Interrupted";
                    }
                }
                else
                {
                    // No finish log found but endDateTime is set - likely interrupted
                    stats.Status = "Interrupted";
                }
            }
            else
            {
                // Execution is still running - check latest milestone to determine current phase
                var latestMilestone = await _logContext.LogEntries
                    .Where(log => log.executionId == executionId && log.action == "Milestone")
                    .OrderByDescending(log => log.datetime)
                    .FirstOrDefaultAsync();

                if (latestMilestone != null)
                {
                    if (latestMilestone.reason.Contains("SourceAnalysisStarted"))
                    {
                        stats.Status = "Analyzing";
                    }
                    else if (latestMilestone.reason.Contains("CopiesStarted"))
                    {
                        stats.Status = "Copying";
                    }
                    else if (latestMilestone.reason.Contains("CopiesFinished"))
                    {
                        stats.Status = "Finalizing";
                    }
                }
                else
                {
                    stats.Status = "Starting";
                }
            }

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
    public string Status { get; set; } = "Unknown";
    public string? CurrentFileName { get; set; }
    public string? CurrentFilePath { get; set; }
    public string RsyncCommand { get; set; } = string.Empty;
    
    // Rsync statistics
    public int TotalFiles { get; set; }
    public int RegularFiles { get; set; }
    public int Directories { get; set; }
    public int CreatedFiles { get; set; }
    public int DeletedFiles { get; set; }
    public int TransferredFiles { get; set; }
    public long TotalFileSize { get; set; }
    public long TotalTransferredSize { get; set; }
    public long LiteralData { get; set; }
    public long MatchedData { get; set; }
    public long FileListSize { get; set; }
    public double FileListGenerationTime { get; set; }
    public double FileListTransferTime { get; set; }
    public long TotalBytesSent { get; set; }
    public long TotalBytesReceived { get; set; }
    public double TransferSpeedBytesPerSecond { get; set; }
    public double Speedup { get; set; }
    public double DurationSeconds { get; set; }
    
    // Progress tracking
    public int? TotalFilesToProcess { get; set; }
    public int CurrentFileIndex { get; set; }
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


