using System.ComponentModel.DataAnnotations.Schema;

namespace server.Models;

[Table("backup_plan")]
public class BackupPlan
{
    public Guid id { get; set; } = new Guid();
    public string name { get; set; } = string.Empty;
    public string description { get; set; } = string.Empty;
    public string schedule { get; set; } = "0 0 * * *";

    public string source { get; set; } = string.Empty;
    public string destination { get; set; } = string.Empty;
    public bool active { get; set; } = false;

    // Rsync connection details
    public string? rsyncHost { get; set; }
    public string? rsyncUser { get; set; }
    public int rsyncPort { get; set; } = 22;
    public string? rsyncSshKey { get; set; } // SSH private key content (not file path)

    // Legacy agent support (optional, for backward compatibility)
    public Agent? agent { get; set; }
}
