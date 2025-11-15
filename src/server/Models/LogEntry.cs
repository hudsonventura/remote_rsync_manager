using System.ComponentModel.DataAnnotations.Schema;

namespace server.Models;

[Table("log_entry")]
public class LogEntry
{
    public Guid id { get; set; } = Guid.NewGuid();
    public Guid backupPlanId { get; set; }
    public DateTime datetime { get; set; } = DateTime.UtcNow;
    public string fileName { get; set; } = string.Empty;
    public string filePath { get; set; } = string.Empty;
    public long? size { get; set; }
    public string action { get; set; } = string.Empty; // "Copy", "Delete", "Ignored"
    public string reason { get; set; } = string.Empty;
}

