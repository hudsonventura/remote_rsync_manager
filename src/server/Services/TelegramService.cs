using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using server.Data;
using Microsoft.EntityFrameworkCore;

namespace server.Services;

public class TelegramService : ITelegramService
{
    private TelegramBotClient? _botClient;
    private readonly ILogger<TelegramService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public TelegramService(
        ILogger<TelegramService> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    private async Task<TelegramBotClient?> GetBotClientAsync()
    {
        if (_botClient != null)
            return _botClient;

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();

        var config = await dbContext.TelegramConfigs.FirstOrDefaultAsync();

        if (config == null || !config.isEnabled || string.IsNullOrEmpty(config.botToken))
        {
            _logger.LogWarning("Telegram bot is not configured or disabled");
            return null;
        }

        _botClient = new TelegramBotClient(config.botToken);
        return _botClient;
    }

    public async Task SendMessageAsync(long chatId, string message)
    {
        try
        {
            var client = await GetBotClientAsync();
            if (client == null)
            {
                _logger.LogWarning("Cannot send message: Telegram bot is not configured");
                return;
            }

            await client.SendMessage(chatId, message);
            _logger.LogInformation("Message sent to chat {ChatId}", chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to chat {ChatId}", chatId);
            throw;
        }
    }

    public async Task SendMessageAsync(string username, string message)
    {
        try
        {
            var client = await GetBotClientAsync();
            if (client == null)
            {
                _logger.LogWarning("Cannot send message: Telegram bot is not configured");
                return;
            }

            await client.SendMessage($"@{username}", message);
            _logger.LogInformation("Message sent to user @{Username}", username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to user @{Username}", username);
            throw;
        }
    }

    public async Task HandleUpdateAsync(Update update)
    {
        try
        {
            if (update.Message is { } message)
            {
                await HandleMessageAsync(message);
            }
            else if (update.CallbackQuery is { } callbackQuery)
            {
                await HandleCallbackQueryAsync(callbackQuery);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update");
        }
    }

    private async Task HandleMessageAsync(Message message)
    {
        if (message.Text is not { } messageText)
            return;

        var client = await GetBotClientAsync();
        if (client == null)
            return;

        var chatId = message.Chat.Id;
        var username = message.From?.Username;

        _logger.LogInformation("Received message from {Username} (ChatId: {ChatId}): {Message}",
            username, chatId, messageText);

        if (messageText.StartsWith("/start"))
        {
            await client.SendMessage(
                chatId,
                "Welcome! This bot is connected to your Remember application.");
        }
        else if (messageText.StartsWith("/help"))
        {
            await client.SendMessage(
                chatId,
                "Available commands:\n" +
                "/start - Start the bot\n" +
                "/help - Show this help message\n" +
                "/status - Check bot status\n" +
                "/chatid - Get your chat ID");
        }
        else if (messageText.StartsWith("/status"))
        {
            await client.SendMessage(
                chatId,
                "Bot is running and connected!");
        }
        else if (messageText.StartsWith("/chatid"))
        {
            await client.SendMessage(
                chatId,
                $"Your Chat ID is: {chatId}");
        }
        else
        {
            await client.SendMessage(
                chatId,
                $"You said: {messageText}");
        }
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery)
    {
        if (callbackQuery.Data is { } data)
        {
            var client = await GetBotClientAsync();
            if (client == null)
                return;

            _logger.LogInformation("Received callback query: {Data}", data);

            await client.AnswerCallbackQuery(callbackQuery.Id);
        }
    }

    public async Task<bool> SetWebhookAsync(string webhookUrl)
    {
        try
        {
            var client = await GetBotClientAsync();
            if (client == null)
            {
                _logger.LogWarning("Cannot set webhook: Telegram bot is not configured");
                return false;
            }

            await client.SetWebhook(webhookUrl);
            _logger.LogInformation("Webhook set to {WebhookUrl}", webhookUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting webhook");
            return false;
        }
    }

    public async Task<bool> DeleteWebhookAsync()
    {
        try
        {
            var client = await GetBotClientAsync();
            if (client == null)
            {
                _logger.LogWarning("Cannot delete webhook: Telegram bot is not configured");
                return false;
            }

            await client.DeleteWebhook();
            _logger.LogInformation("Webhook deleted");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting webhook");
            return false;
        }
    }
}
