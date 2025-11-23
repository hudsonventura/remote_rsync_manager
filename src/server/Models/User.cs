using System.ComponentModel.DataAnnotations.Schema;

namespace server.Models;

[Table("user")]
public class User
{
    public Guid id { get; set; } = Guid.NewGuid();
    public string username { get; set; } = string.Empty;
    public string email { get; set; } = string.Empty;
    public string passwordHash { get; set; } = string.Empty;
    public bool isAdmin { get; set; } = false;
    public DateTime createdAt { get; set; } = DateTime.UtcNow;
    public DateTime? updatedAt { get; set; }
    public bool isActive { get; set; } = true;
    public string? timezone { get; set; }
    public string? theme { get; set; }
}

