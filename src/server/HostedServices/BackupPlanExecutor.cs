using System.Security.Cryptography.X509Certificates;
using server.Data;
using server.Models;
using server.Services;
using System.Security.Cryptography;

namespace server.HostedServices;

public class BackupPlanExecutor
{
    private readonly ILogger<BackupPlanExecutor> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IWebHostEnvironment _environment;

    public BackupPlanExecutor(
        ILogger<BackupPlanExecutor> logger,
        IServiceScopeFactory serviceScopeFactory,
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _environment = environment;
    }

    public async Task<ExecutionResult> ExecuteBackupPlanAsync(BackupPlan backupPlan, bool isAutomatic = true, bool isSimulation = false)
    {

        return new ExecutionResult();
    }

    public async Task<ExecutionResult> SimulateBackupPlanAsync(BackupPlan backupPlan)
    {
        return await ExecuteBackupPlanAsync(backupPlan, false, true);
    }




}
