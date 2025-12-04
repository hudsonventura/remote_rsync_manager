using server.Models;
using server.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace server.Services;

public class NotificationService : INotificationService
{
    private readonly ITelegramService _telegramService;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ITelegramService telegramService,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<NotificationService> logger)
    {
        _telegramService = telegramService;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task SendBackupStartNotificationAsync(BackupPlan backupPlan, bool isAutomatic, bool isSimulation)
    {
        try
        {
            var config = await GetTelegramConfigAsync();
            if (config == null || !config.notificationsEnabled || string.IsNullOrEmpty(config.notificationChatId))
            {
                return;
            }

            var executionType = isSimulation ? "🔍 Simulation" : (isAutomatic ? "⏰ Automatic" : "▶️ Manual");
            var message = new StringBuilder();
            message.AppendLine($"{executionType} Backup Started");
            message.AppendLine();
            message.AppendLine($"📋 Plan: {backupPlan.name}");
            message.AppendLine($"📁 Source: {backupPlan.source}");
            message.AppendLine($"💾 Destination: {backupPlan.destination}");
            message.AppendLine($"🖥️ Host: {backupPlan.rsyncHost}");
            message.AppendLine($"🕐 Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            if (!string.IsNullOrEmpty(backupPlan.description))
            {
                message.AppendLine($"📝 Description: {backupPlan.description}");
            }

            await _telegramService.SendMessageAsync(long.Parse(config.notificationChatId), message.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send backup start notification");
        }
    }

    public async Task SendBackupCompletedNotificationAsync(BackupPlan backupPlan, ExecutionResult result, bool isAutomatic, bool isSimulation)
    {
        try
        {
            var config = await GetTelegramConfigAsync();
            if (config == null || !config.notificationsEnabled || string.IsNullOrEmpty(config.notificationChatId))
            {
                return;
            }

            var executionType = isSimulation ? "🔍 Simulation" : (isAutomatic ? "⏰ Automatic" : "▶️ Manual");
            var message = new StringBuilder();
            message.AppendLine($"✅ {executionType} Backup Completed");
            message.AppendLine();
            message.AppendLine($"📋 Plan: {backupPlan.name}");
            message.AppendLine($"📁 Source: {backupPlan.source}");
            message.AppendLine($"💾 Destination: {backupPlan.destination}");
            message.AppendLine();
            message.AppendLine("📊 Statistics:");
            message.AppendLine($"  • Total Files: {result.TotalFiles:N0}");
            message.AppendLine($"  • Created: {result.CreatedFiles:N0}");
            message.AppendLine($"  • Transferred: {result.TransferredFiles:N0}");
            message.AppendLine($"  • Deleted: {result.DeletedFiles:N0}");
            message.AppendLine($"  • Total Size: {FormatBytes(result.TotalFileSize)}");
            message.AppendLine($"  • Transferred: {FormatBytes(result.TotalTransferredSize)}");
            
            if (result.DurationSeconds > 0)
            {
                message.AppendLine($"  • Duration: {FormatDuration(result.DurationSeconds)}");
            }
            
            if (result.TransferSpeedBytesPerSecond > 0)
            {
                message.AppendLine($"  • Speed: {FormatBytes((long)result.TransferSpeedBytesPerSecond)}/s");
            }

            message.AppendLine($"🕐 Completed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            await _telegramService.SendMessageAsync(long.Parse(config.notificationChatId), message.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send backup completed notification");
        }
    }

    public async Task SendBackupFailedNotificationAsync(BackupPlan backupPlan, string error, bool isAutomatic, bool isSimulation)
    {
        try
        {
            var config = await GetTelegramConfigAsync();
            if (config == null || !config.notificationsEnabled || string.IsNullOrEmpty(config.notificationChatId))
            {
                return;
            }

            var executionType = isSimulation ? "🔍 Simulation" : (isAutomatic ? "⏰ Automatic" : "▶️ Manual");
            var message = new StringBuilder();
            message.AppendLine($"❌ {executionType} Backup Failed");
            message.AppendLine();
            message.AppendLine($"📋 Plan: {backupPlan.name}");
            message.AppendLine($"📁 Source: {backupPlan.source}");
            message.AppendLine($"💾 Destination: {backupPlan.destination}");
            message.AppendLine($"🖥️ Host: {backupPlan.rsyncHost}");
            message.AppendLine();
            message.AppendLine($"⚠️ Error: {error}");
            message.AppendLine($"🕐 Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            await _telegramService.SendMessageAsync(long.Parse(config.notificationChatId), message.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send backup failed notification");
        }
    }

    private async Task<TelegramConfig?> GetTelegramConfigAsync()
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();
        return await dbContext.TelegramConfigs.FirstOrDefaultAsync();
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
            size /= 1024;
        }
        
        return $"{size:0.##} {sizes[order]}";
    }

    private string FormatDuration(double seconds)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        
        if (timeSpan.TotalHours >= 1)
        {
            return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
        }
        else if (timeSpan.TotalMinutes >= 1)
        {
            return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
        }
        else
        {
            return $"{timeSpan.Seconds}s";
        }
    }
}
