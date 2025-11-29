using System.Security.Cryptography.X509Certificates;
using server.Data;
using server.Models;
using server.Services;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

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
        Agent? agent = backupPlan.agent;

        if (string.IsNullOrWhiteSpace(agent.rsyncSshKey))
        {
            _logger.LogError("Agent {AgentId} does not have an SSH key configured", agent.id);
            throw new InvalidOperationException("Agent does not have an SSH key configured");
        }
        var sshKeyPath = Path.Combine(Path.GetTempPath(), $"ssh_key_{Guid.NewGuid()}");
        var result = new ExecutionResult();

        try
        {
            // Write SSH key to temporary file
            await File.WriteAllTextAsync(sshKeyPath, agent.rsyncSshKey);
            File.SetUnixFileMode(sshKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            // Build rsync command
            var rsyncArgs = new StringBuilder();
            
            if (isSimulation)
            {
                rsyncArgs.Append("--dry-run ");
            }
            
            rsyncArgs.Append("-avz --progress ");
            
            // Build SSH command for -e option
            var sshCommand = $"ssh -i {sshKeyPath} -p {agent.rsyncPort} -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null";
            rsyncArgs.Append($"-e \"{sshCommand}\" ");
            
            var sourcePath = $"{agent.rsyncUser}@{agent.hostname}:{backupPlan.source}";
            
            rsyncArgs.Append($"{backupPlan.destination} {sourcePath}");

            _logger.LogInformation("Executing rsync: rsync {RsyncArgs}", rsyncArgs.ToString());

            // Execute rsync
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "rsync",
                Arguments = rsyncArgs.ToString(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    _logger.LogDebug("Rsync output: {Output}", e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                    _logger.LogDebug("Rsync error: {Error}", e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            if (process.ExitCode != 0)
            {
                _logger.LogError("Rsync failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, error);
                throw new Exception($"Rsync failed: {error}");
            }

            // Parse output for simulation mode
            if (isSimulation)
            {
                ParseRsyncOutput(output, result);
            }
            else
            {
                _logger.LogInformation("Backup completed successfully");
            }

            return result;
        }
        finally
        {
            // Clean up temporary SSH key file
            try
            {
                if (File.Exists(sshKeyPath))
                {
                    File.Delete(sshKeyPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temporary SSH key file: {SshKeyPath}", sshKeyPath);
            }
        }
    }

    private void ParseRsyncOutput(string output, ExecutionResult result)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var items = new List<ExecutionItems>();

        foreach (var line in lines)
        {
            // Parse rsync output lines (e.g., ">f+++++++++ file.txt")
            if (line.StartsWith(">") || line.StartsWith("<"))
            {
                var match = Regex.Match(line, @"^[<>]([fd])([.+\-]+)\s+(.+)$");
                if (match.Success)
                {
                    var itemType = match.Groups[1].Value; // 'f' for file, 'd' for directory
                    var flags = match.Groups[2].Value;
                    var path = match.Groups[3].Value.Trim();

                    var item = new ExecutionItems
                    {
                        FilePath = path,
                        FileName = Path.GetFileName(path),
                        Action = "Copy",
                        Reason = itemType == "d" ? "Directory" : "File"
                    };

                    items.Add(item);
                }
            }
        }

        result.Items = items;
        result.TotalItems = items.Count;
        result.ItemsToCopy = items.Count;
        result.ItemsToDelete = 0;
    }

    public async Task<ExecutionResult> SimulateBackupPlanAsync(BackupPlan backupPlan)
    {
        return await ExecuteBackupPlanAsync(backupPlan, false, true);
    }




}
