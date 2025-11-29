using System.ComponentModel.DataAnnotations.Schema;

namespace server.Models;

[Table("log_entry")]
public class LogEntry
{
    public Guid id { get; set; } = Guid.NewGuid();
    public Guid backupPlanId { get; set; }
    public Guid executionId { get; set; }
    public DateTime datetime { get; set; } = DateTime.UtcNow;
    public string fileName { get; set; } = string.Empty;
    public string filePath { get; set; } = string.Empty;
    public long? size { get; set; }
    public string action
    {
        get
        {
            return _action.ToString();
        }
        set
        {
            _action = Enum.Parse<Action>(value);
        }
    }

    private Action _action;



    [Column("reason")]
    public string reason { get; set; } = string.Empty; // "File not found", "File not readable", "File not writable", "File not executable", "File not a directory", "File not a file", "File not a symlink", "File not a socket", "File not a pipe", "File not a block device", "File not a character device", "File not a FIFO", "File not a socket", "File not a pipe", "File not a block device", "File not a character device", "File not a FIFO", "File not a socket", "File not a pipe", "File not a block device", "File not a character device", "File not a FIFO"


    public enum Action
    {
        Copy,
        Delete,
        Ignored,
        CopyError,
        DeleteError,
        CopySkipped,
        DeleteSkipped,
        System
    }
}



