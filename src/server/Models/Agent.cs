using System.ComponentModel.DataAnnotations.Schema;

namespace server.Models;

[Table("agent")]
public class Agent
{
    public Guid id { get; set; } = new Guid();
    public string name { get; set; } = "New Agent";
    public string hostname { get; set; } = string.Empty;
    public string? token { get; set; }
    
    // Rsync/SSH connection details
    public string? rsyncUser { get; set; }
    public int rsyncPort { get; set; } = 22;
    public string? rsyncSshKey { get; set; } // SSH private key content (not file path)
}
