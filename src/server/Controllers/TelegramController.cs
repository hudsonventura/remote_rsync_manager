using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types;
using server.Services;
using server.Data;
using Microsoft.EntityFrameworkCore;

namespace server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TelegramController : ControllerBase
{
    private readonly ITelegramService _telegramService;
    private readonly ILogger<TelegramController> _logger;
    private readonly DBContext _context;

    public TelegramController(
        ITelegramService telegramService,
        ILogger<TelegramController> logger,
        DBContext context)
    {
        _telegramService = telegramService;
        _logger = logger;
        _context = context;
    }

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig()
    {
        try
        {
            var config = await _context.TelegramConfigs.FirstOrDefaultAsync();

            if (config == null)
            {
                return Ok(new TelegramConfigResponse
                {
                    IsEnabled = false,
                    BotToken = string.Empty,
                    WebhookUrl = string.Empty,
                    NotificationsEnabled = false,
                    NotificationChatId = string.Empty
                });
            }

            return Ok(new TelegramConfigResponse
            {
                IsEnabled = config.isEnabled,
                BotToken = config.botToken,
                WebhookUrl = config.webhookUrl,
                NotificationsEnabled = config.notificationsEnabled,
                NotificationChatId = config.notificationChatId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Telegram config");
            return StatusCode(500, new { message = "Error retrieving Telegram configuration", error = ex.Message });
        }
    }

    [HttpPost("config")]
    public async Task<IActionResult> SaveConfig([FromBody] SaveTelegramConfigRequest request)
    {
        try
        {
            var config = await _context.TelegramConfigs.FirstOrDefaultAsync();

            if (config == null)
            {
                config = new server.Models.TelegramConfig
                {
                    botToken = request.BotToken,
                    webhookUrl = request.WebhookUrl ?? string.Empty,
                    isEnabled = request.IsEnabled,
                    notificationsEnabled = request.NotificationsEnabled,
                    notificationChatId = request.NotificationChatId ?? string.Empty,
                    created_at = DateTime.UtcNow,
                    updated_at = DateTime.UtcNow
                };
                _context.TelegramConfigs.Add(config);
            }
            else
            {
                config.botToken = request.BotToken;
                config.webhookUrl = request.WebhookUrl ?? string.Empty;
                config.isEnabled = request.IsEnabled;
                config.notificationsEnabled = request.NotificationsEnabled;
                config.notificationChatId = request.NotificationChatId ?? string.Empty;
                config.updated_at = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Telegram configuration saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving Telegram config");
            return StatusCode(500, new { success = false, message = "Error saving Telegram configuration", error = ex.Message });
        }
    }

    [HttpPost("test")]
    public async Task<IActionResult> TestConnection([FromBody] TestTelegramRequest request)
    {
        try
        {
            await _telegramService.SendMessageAsync(request.ChatId, "Test message from Remember application!");
            return Ok(new { success = true, message = "Test message sent successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending test message");
            return StatusCode(500, new { success = false, message = "Error sending test message", error = ex.Message });
        }
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] Update update)
    {
        try
        {
            await _telegramService.HandleUpdateAsync(update);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook");
            return StatusCode(500);
        }
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        try
        {
            if (request.ChatId.HasValue)
            {
                await _telegramService.SendMessageAsync(request.ChatId.Value, request.Message);
            }
            else if (!string.IsNullOrEmpty(request.Username))
            {
                await _telegramService.SendMessageAsync(request.Username, request.Message);
            }
            else
            {
                return BadRequest("Either ChatId or Username must be provided");
            }

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    [HttpPost("webhook/set")]
    public async Task<IActionResult> SetWebhook([FromBody] SetWebhookRequest request)
    {
        try
        {
            var success = await _telegramService.SetWebhookAsync(request.WebhookUrl);
            return Ok(new { success });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting webhook");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }

    [HttpPost("webhook/delete")]
    public async Task<IActionResult> DeleteWebhook()
    {
        try
        {
            var success = await _telegramService.DeleteWebhookAsync();
            return Ok(new { success });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting webhook");
            return StatusCode(500, new { success = false, error = ex.Message });
        }
    }
}

public class TelegramConfigResponse
{
    public bool IsEnabled { get; set; }
    public string BotToken { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public bool NotificationsEnabled { get; set; }
    public string NotificationChatId { get; set; } = string.Empty;
}

public class SaveTelegramConfigRequest
{
    public string BotToken { get; set; } = string.Empty;
    public string? WebhookUrl { get; set; }
    public bool IsEnabled { get; set; }
    public bool NotificationsEnabled { get; set; }
    public string? NotificationChatId { get; set; }
}

public class TestTelegramRequest
{
    public long ChatId { get; set; }
}

public class SendMessageRequest
{
    public long? ChatId { get; set; }
    public string? Username { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class SetWebhookRequest
{
    public string WebhookUrl { get; set; } = string.Empty;
}
