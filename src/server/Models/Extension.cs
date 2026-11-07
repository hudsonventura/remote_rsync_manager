using System.ComponentModel.DataAnnotations.Schema;

namespace server.Models;

[Table("extension")]
public class Extension
{
    public Guid id { get; set; } = Guid.NewGuid();
    public string name { get; set; } = string.Empty;
    public string? description { get; set; }
    public string command { get; set; } = "dotnet App.dll";
    public string schedule { get; set; } = "0 * * * *";
    public bool active { get; set; } = true;
    public string folderPath { get; set; } = string.Empty;
    public string? environmentVariables { get; set; } // JSON serialized key-value pairs
    public DateTime createdAt { get; set; } = DateTime.UtcNow;
    public DateTime updatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? lastRunAt { get; set; }
    public int? lastExitCode { get; set; }
    public string? lastStatus { get; set; } // "Never Run", "Running", "Success", "Failed"
}

