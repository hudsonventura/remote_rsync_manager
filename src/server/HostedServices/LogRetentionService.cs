using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using server.Data;
using server.Models;

namespace server.HostedServices;

public class LogRetentionService : IHostedService
{
    private readonly ILogger<LogRetentionService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IWebHostEnvironment _environment;
    private Timer? _timer;

    public LogRetentionService(
        ILogger<LogRetentionService> logger,
        IServiceScopeFactory serviceScopeFactory,
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _environment = environment;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("LogRetentionService is starting...");
        
        // Run immediately on startup, then every hour
        _timer = new Timer(ExecuteLogRetention, null, TimeSpan.Zero, TimeSpan.FromHours(1));
        
        _logger.LogInformation("LogRetentionService started. Will run every hour.");
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("LogRetentionService is stopping...");
        
        _timer?.Change(Timeout.Infinite, 0);
        
        return Task.CompletedTask;
    }

    private void ExecuteLogRetention(object? state)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();
                var logContext = scope.ServiceProvider.GetRequiredService<LogDbContext>();

                // Get retention period setting
                var retentionSetting = await dbContext.AppSettings
                    .FirstOrDefaultAsync(s => s.key == "LogRetentionPeriodMonths");

                if (retentionSetting == null || string.IsNullOrEmpty(retentionSetting.value) || !int.TryParse(retentionSetting.value, out var months) || months <= 0)
                {
                    _logger.LogDebug("Log retention period not configured or disabled. Skipping automatic deletion.");
                    return;
                }

                // Calculate cutoff date
                var cutoffDate = DateTime.UtcNow.AddMonths(-months).Date;
                
                _logger.LogInformation("Starting automatic log retention cleanup. Deleting logs older than {CutoffDate} (retention period: {Months} months)", cutoffDate, months);

                // Get executions that started before the cutoff date
                var executionsToDelete = await logContext.BackupExecutions
                    .Where(e => e.startDateTime < cutoffDate)
                    .Select(e => e.id)
                    .ToListAsync();

                if (executionsToDelete.Count == 0)
                {
                    _logger.LogInformation("No old logs to delete.");
                    return;
                }

                // Delete log entries for those executions
                var logsDeleted = await logContext.LogEntries
                    .Where(log => executionsToDelete.Contains(log.executionId))
                    .CountAsync();

                logContext.LogEntries.RemoveRange(
                    logContext.LogEntries.Where(log => executionsToDelete.Contains(log.executionId))
                );

                // Delete executions
                logContext.BackupExecutions.RemoveRange(
                    logContext.BackupExecutions.Where(e => executionsToDelete.Contains(e.id))
                );

                await logContext.SaveChangesAsync();

                // Delete notifications for deleted executions
                var notificationsDeleted = await dbContext.Notifications
                    .Where(n => n.executionId.HasValue && executionsToDelete.Contains(n.executionId.Value))
                    .CountAsync();

                dbContext.Notifications.RemoveRange(
                    dbContext.Notifications.Where(n => n.executionId.HasValue && executionsToDelete.Contains(n.executionId.Value))
                );

                await dbContext.SaveChangesAsync();

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
                        await logContext.Database.CloseConnectionAsync();

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

                        _logger.LogInformation(
                            "Automatic log retention cleanup completed. Deleted {ExecutionsCount} executions, {LogsCount} log entries, and {NotificationsCount} notifications. Space saved: {SpaceSaved}",
                            executionsToDelete.Count,
                            logsDeleted,
                            notificationsDeleted,
                            FormatBytes(spaceSaved));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to execute VACUUM on {DbPath}", dbPath);
                        _logger.LogInformation(
                            "Automatic log retention cleanup completed. Deleted {ExecutionsCount} executions, {LogsCount} log entries, and {NotificationsCount} notifications.",
                            executionsToDelete.Count,
                            logsDeleted,
                            notificationsDeleted);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during automatic log retention cleanup");
            }
        });
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
}

