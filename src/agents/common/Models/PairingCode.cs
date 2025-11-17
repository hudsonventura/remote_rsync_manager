using System.ComponentModel.DataAnnotations.Schema;

namespace AgentCommon.Models;

[Table("pairing_code")]
public class PairingCode
{
    public int id { get; set; }
    public string code { get; set; } = string.Empty;
    public DateTime created_at { get; set; } = DateTime.UtcNow;
    public DateTime expires_at { get; set; }
}

