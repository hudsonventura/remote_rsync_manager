using System.ComponentModel.DataAnnotations.Schema;

namespace server.Models;

[Table("agent")]
public class Agent
{
    public Guid id { get; set; } = new Guid();
    public string hostname { get; set; } = string.Empty;
}
