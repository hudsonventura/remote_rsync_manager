using System.ComponentModel.DataAnnotations.Schema;

namespace server.Models;

[Table("telegram_config")]
public class TelegramConfig
{
    public Guid id { get; set; } = Guid.NewGuid();
    public string botToken { get; set; } = string.Empty;
    public string webhookUrl { get; set; } = string.Empty;
    public bool isEnabled { get; set; } = false;
    public bool notificationsEnabled { get; set; } = false;
    public string notificationChatId { get; set; } = string.Empty;
    public DateTime created_at { get; set; } = DateTime.UtcNow;
    public DateTime updated_at { get; set; } = DateTime.UtcNow;
}
