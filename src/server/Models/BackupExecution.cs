using System.ComponentModel.DataAnnotations.Schema;

namespace server.Models;

[Table("backup_execution")]
public class BackupExecution
{
    public Guid id { get; set; } = Guid.NewGuid();
    public Guid backupPlanId { get; set; }
    public string name { get; set; } = string.Empty;
    public DateTime startDateTime { get; set; } = DateTime.UtcNow;
    public DateTime? endDateTime { get; set; }
    public string? currentFileName { get; set; }
    public string? currentFilePath { get; set; }
}

