using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NCrontab;
using server.Data;
using server.Models;

namespace server.HostedServices;

public class BackupRunner : IHostedService
{
    private readonly ILogger<BackupRunner> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private Timer? _timer;
    private readonly Dictionary<Guid, CrontabSchedule> _cronSchedules = new();

    public BackupRunner(ILogger<BackupRunner> logger, IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BackupRunner is starting...");
        
        // Load all backup plans and parse cron strings
        LoadBackupPlans();
        
        // Start a timer to check cron schedules every minute
        _timer = new Timer(CheckCronSchedules, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        
        _logger.LogInformation("BackupRunner started. Monitoring {Count} backup plans", _cronSchedules.Count);
        
        return Task.CompletedTask;
    }

    private void LoadBackupPlans()
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();
            
            var backupPlans = dbContext.BackupPlans
                .Where(bp => bp.active)
                .ToList();
            
            _cronSchedules.Clear();
            
            foreach (var plan in backupPlans)
            {
                try
                {
                    var cronSchedule = CrontabSchedule.Parse(plan.schedule);
                    _cronSchedules[plan.id] = cronSchedule;
                    _logger.LogInformation("Loaded backup plan '{Name}' (ID: {Id}) with schedule: {Schedule}", 
                        plan.name, plan.id, plan.schedule);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse cron schedule '{Schedule}' for backup plan '{Name}' (ID: {Id})", 
                        plan.schedule, plan.name, plan.id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading backup plans");
        }
    }

    private void CheckCronSchedules(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            
            // Reload backup plans at the beginning of each minute
            LoadBackupPlans();
            
            // Get backup plans for checking schedules
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();
            
            var backupPlans = dbContext.BackupPlans
                .Where(bp => bp.active)
                .ToList();
            
            // Check each cron schedule
            foreach (var kvp in _cronSchedules)
            {
                var planId = kvp.Key;
                var cronSchedule = kvp.Value;
                
                var plan = backupPlans.FirstOrDefault(p => p.id == planId);
                if (plan == null) continue;
                
                // Get the next occurrence
                var nextOccurrence = cronSchedule.GetNextOccurrence(now.AddMinutes(-1));
                
                // Check if the current minute matches the schedule
                if (nextOccurrence <= now && (now - nextOccurrence).TotalMinutes < 1)
                {
                    // Check if rsync host is configured
                    if (string.IsNullOrWhiteSpace(plan.rsyncHost))
                    {
                        _logger.LogWarning("Backup plan '{Name}' (ID: {Id}) is due to run but has no rsync host configured. Skipping execution.", 
                            plan.name, plan.id);
                        continue;
                    }
                    
                    _logger.LogInformation("Backup plan '{Name}' (ID: {Id}) is due to run. Schedule: {Schedule}, RsyncHost: {RsyncHost}", 
                        plan.name, plan.id, plan.schedule, plan.rsyncHost);
                    
                    // Start backup execution asynchronously
                    // Create a new scope for the async execution to avoid disposing the scope too early
                    var planToExecute = plan; // Capture the plan for the closure
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var executionScope = _serviceScopeFactory.CreateScope();
                            var executor = executionScope.ServiceProvider.GetRequiredService<BackupPlanExecutor>();
                            await executor.ExecuteBackupPlanAsync(planToExecute);
                            _logger.LogInformation("Backup plan '{Name}' (ID: {Id}) execution completed", 
                                planToExecute.name, planToExecute.id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error executing backup plan '{Name}' (ID: {Id})", 
                                planToExecute.name, planToExecute.id);
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cron schedules");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BackupRunner is stopping...");
        
        _timer?.Change(Timeout.Infinite, 0);
        _timer?.Dispose();
        
        _logger.LogInformation("BackupRunner stopped");
        
        return Task.CompletedTask;
    }
}
