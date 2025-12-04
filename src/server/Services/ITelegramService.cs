using Telegram.Bot.Types;

namespace server.Services;

public interface ITelegramService
{
    Task SendMessageAsync(long chatId, string message);
    Task SendMessageAsync(string username, string message);
    Task HandleUpdateAsync(Update update);
    Task<bool> SetWebhookAsync(string webhookUrl);
    Task<bool> DeleteWebhookAsync();
}
