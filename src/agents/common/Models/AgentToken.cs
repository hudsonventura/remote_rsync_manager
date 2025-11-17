using System.ComponentModel.DataAnnotations.Schema;

namespace AgentCommon.Models;

[Table("agent_token")]
public class AgentToken
{
    public int id { get; set; }
    public string token { get; set; } = string.Empty;
    public DateTime created_at { get; set; } = DateTime.UtcNow;
}

