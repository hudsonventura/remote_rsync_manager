using Microsoft.EntityFrameworkCore;
using server.Data;
using server.Models;

namespace server.Services;

public interface INotificationService
{
    Task CreateBackupCompletedNotificationAsync(Guid backupPlanId, Guid executionId, string backupPlanName, bool success, string? errorMessage = null);
    Task CreateSimulationCompletedNotificationAsync(Guid backupPlanId, Guid executionId, string backupPlanName, int totalItems, int itemsToCopy, int itemsToDelete);
}

public class NotificationService : INotificationService
{
    private readonly DBContext _context;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(DBContext context, ILogger<NotificationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task CreateBackupCompletedNotificationAsync(Guid backupPlanId, Guid executionId, string backupPlanName, bool success, string? errorMessage = null)
    {
        try
        {
            var notification = new Notification
            {
                id = Guid.NewGuid(),
                type = "BackupCompleted",
                title = success 
                    ? $"Backup Completed: {backupPlanName}"
                    : $"Backup Failed: {backupPlanName}",
                message = success
                    ? $"Backup plan '{backupPlanName}' has completed successfully."
                    : $"Backup plan '{backupPlanName}' failed. {(errorMessage != null ? $"Error: {errorMessage}" : "")}",
                backupPlanId = backupPlanId,
                executionId = executionId,
                isRead = false,
                createdAt = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created backup completion notification for plan {BackupPlanId}", backupPlanId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backup completion notification for plan {BackupPlanId}", backupPlanId);
        }
    }

    public async Task CreateSimulationCompletedNotificationAsync(Guid backupPlanId, Guid executionId, string backupPlanName, int totalItems, int itemsToCopy, int itemsToDelete)
    {
        try
        {
            var notification = new Notification
            {
                id = Guid.NewGuid(),
                type = "SimulationCompleted",
                title = $"Simulation Completed: {backupPlanName}",
                message = $"Simulation for backup plan '{backupPlanName}' completed. Total items: {totalItems}, To copy: {itemsToCopy}, To delete: {itemsToDelete}",
                backupPlanId = backupPlanId,
                executionId = executionId,
                isRead = false,
                createdAt = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created simulation completion notification for plan {BackupPlanId}", backupPlanId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create simulation completion notification for plan {BackupPlanId}", backupPlanId);
        }
    }
}

