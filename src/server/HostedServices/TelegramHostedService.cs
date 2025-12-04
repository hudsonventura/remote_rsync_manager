using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using server.Services;
using server.Data;
using Microsoft.EntityFrameworkCore;

namespace server.HostedServices;

public class TelegramHostedService : IHostedService
{
    private readonly ILogger<TelegramHostedService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public TelegramHostedService(
        ILogger<TelegramHostedService> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TelegramHostedService is starting...");

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();

            var config = await dbContext.TelegramConfigs.FirstOrDefaultAsync(cancellationToken);

            if (config == null || !config.isEnabled || string.IsNullOrEmpty(config.botToken))
            {
                _logger.LogWarning("Telegram bot is not configured or disabled. Skipping initialization.");
                return;
            }

            var telegramService = scope.ServiceProvider.GetRequiredService<ITelegramService>();

            if (!string.IsNullOrEmpty(config.webhookUrl))
            {
                await telegramService.SetWebhookAsync(config.webhookUrl);
                _logger.LogInformation("Telegram webhook configured: {WebhookUrl}", config.webhookUrl);
            }
            else
            {
                _logger.LogInformation("Telegram bot started in polling mode (no webhook configured)");
            }

            _logger.LogInformation("TelegramHostedService started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting TelegramHostedService");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TelegramHostedService is stopping...");
        
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var telegramService = scope.ServiceProvider.GetRequiredService<ITelegramService>();
            
            await telegramService.DeleteWebhookAsync();
            _logger.LogInformation("Telegram webhook removed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping TelegramHostedService");
        }
    }
}
