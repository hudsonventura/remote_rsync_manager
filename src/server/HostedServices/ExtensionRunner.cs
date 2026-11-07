using Microsoft.EntityFrameworkCore;
using NCrontab;
using server.Data;
using server.Models;
using server.Services;

namespace server.HostedServices;

public class ExtensionRunner : IHostedService
{
    private readonly ILogger<ExtensionRunner> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private Timer? _timer;
    private readonly Dictionary<Guid, CrontabSchedule> _cronSchedules = new();

    public ExtensionRunner(ILogger<ExtensionRunner> logger, IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ExtensionRunner is starting...");

        LoadExtensionSchedules();

        // Check cron schedules every minute
        _timer = new Timer(CheckCronSchedules, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

        _logger.LogInformation("ExtensionRunner started. Monitoring {Count} extensions", _cronSchedules.Count);

        return Task.CompletedTask;
    }

    private void LoadExtensionSchedules()
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();

            var extensions = dbContext.Extensions
                .Where(e => e.active)
                .ToList();

            _cronSchedules.Clear();

            foreach (var ext in extensions)
            {
                try
                {
                    var cronSchedule = CrontabSchedule.Parse(ext.schedule);
                    _cronSchedules[ext.id] = cronSchedule;
                    _logger.LogInformation("Loaded extension '{Name}' ({Id}) with schedule: {Schedule}",
                        ext.name, ext.id, ext.schedule);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse cron schedule '{Schedule}' for extension '{Name}' ({Id})",
                        ext.schedule, ext.name, ext.id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading extension schedules");
        }
    }

    private void CheckCronSchedules(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;

            LoadExtensionSchedules();

            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();

            var extensions = dbContext.Extensions
                .Where(e => e.active)
                .ToList();

            foreach (var kvp in _cronSchedules)
            {
                var extensionId = kvp.Key;
                var cronSchedule = kvp.Value;

                var ext = extensions.FirstOrDefault(e => e.id == extensionId);
                if (ext == null) continue;

                var nextOccurrence = cronSchedule.GetNextOccurrence(now.AddMinutes(-1));

                if (nextOccurrence <= now && (now - nextOccurrence).TotalMinutes < 1)
                {
                    _logger.LogInformation("Extension '{Name}' ({Id}) is due to run. Schedule: {Schedule}",
                        ext.name, ext.id, ext.schedule);

                    var idToExecute = ext.id;
                    var nameToExecute = ext.name;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var execScope = _serviceScopeFactory.CreateScope();
                            var executor = execScope.ServiceProvider.GetRequiredService<ExtensionExecutor>();
                            await executor.ExecuteAsync(idToExecute, isAutomatic: true);
                            _logger.LogInformation("Extension '{Name}' ({Id}) scheduled execution completed",
                                nameToExecute, idToExecute);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error executing extension '{Name}' ({Id})",
                                nameToExecute, idToExecute);
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking extension cron schedules");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ExtensionRunner is stopping...");

        _timer?.Change(Timeout.Infinite, 0);
        _timer?.Dispose();

        _logger.LogInformation("ExtensionRunner stopped");

        return Task.CompletedTask;
    }
}

