using System.ComponentModel.DataAnnotations.Schema;

namespace server.Models;

[Table("extension_execution")]
public class ExtensionExecution
{
    public Guid id { get; set; } = Guid.NewGuid();
    public Guid extensionId { get; set; }
    public string extensionName { get; set; } = string.Empty;
    public string command { get; set; } = string.Empty;
    public DateTime startDateTime { get; set; } = DateTime.UtcNow;
    public DateTime? endDateTime { get; set; }
    public int? exitCode { get; set; }
    public string? output { get; set; }
    public string status { get; set; } = "Running"; // "Running", "Success", "Failed"
    public bool isAutomatic { get; set; } = true;
}

