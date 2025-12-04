using server.Models;

namespace server.Services;

public interface INotificationService
{
    Task SendBackupStartNotificationAsync(BackupPlan backupPlan, bool isAutomatic, bool isSimulation);
    Task SendBackupCompletedNotificationAsync(BackupPlan backupPlan, ExecutionResult result, bool isAutomatic, bool isSimulation);
    Task SendBackupFailedNotificationAsync(BackupPlan backupPlan, string error, bool isAutomatic, bool isSimulation);
}
