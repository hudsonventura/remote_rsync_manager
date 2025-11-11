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
                .Include(bp => bp.agent)
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
            
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();
            
            // Reload backup plans to get any updates
            var backupPlans = dbContext.BackupPlans
                .Include(bp => bp.agent)
                .ToList();
            
            // Update cron schedules for new or modified plans
            foreach (var plan in backupPlans)
            {
                if (!_cronSchedules.ContainsKey(plan.id))
                {
                    try
                    {
                        var cronSchedule = CrontabSchedule.Parse(plan.schedule);
                        _cronSchedules[plan.id] = cronSchedule;
                        _logger.LogInformation("Added new backup plan '{Name}' (ID: {Id}) with schedule: {Schedule}", 
                            plan.name, plan.id, plan.schedule);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to parse cron schedule '{Schedule}' for backup plan '{Name}' (ID: {Id})", 
                            plan.schedule, plan.name, plan.id);
                    }
                }
            }
            
            // Remove plans that no longer exist
            var existingPlanIds = backupPlans.Select(p => p.id).ToHashSet();
            var plansToRemove = _cronSchedules.Keys.Where(id => !existingPlanIds.Contains(id)).ToList();
            foreach (var planId in plansToRemove)
            {
                _cronSchedules.Remove(planId);
                _logger.LogInformation("Removed backup plan (ID: {Id}) from monitoring", planId);
            }
            
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
                    // Check if agent exists
                    if (plan.agent == null)
                    {
                        _logger.LogWarning("Backup plan '{Name}' (ID: {Id}) is due to run but has no associated agent. Skipping execution.", 
                            plan.name, plan.id);
                        continue;
                    }
                    
                    _logger.LogInformation("Backup plan '{Name}' (ID: {Id}) is due to run. Schedule: {Schedule}, Agent: {AgentHostname}", 
                        plan.name, plan.id, plan.schedule, plan.agent.hostname);
                    
                    // Start backup execution asynchronously
                    // Create a new scope for the async execution to avoid disposing the scope too early
                    var planToExecute = plan; // Capture the plan for the closure
                    var agentToExecute = plan.agent; // Capture the agent for the closure
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var executionScope = _serviceScopeFactory.CreateScope();
                            var executor = executionScope.ServiceProvider.GetRequiredService<BackupPlanExecutor>();
                            await executor.ExecuteBackupPlanAsync(planToExecute, agentToExecute);
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
