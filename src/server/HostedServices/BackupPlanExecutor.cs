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

        if (agent == null)
        {
            _logger.LogError("Backup plan {BackupPlanId} does not have an agent configured", backupPlan.id);
            throw new InvalidOperationException("Backup plan does not have an agent configured");
        }

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
            
            rsyncArgs.Append("-avz --progress --delete --itemize-changes --stats ");
            
            // Build SSH command for -e option
            var sshCommand = $"ssh -i {sshKeyPath} -p {agent.rsyncPort} -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null";
            rsyncArgs.Append($"-e \"{sshCommand}\" ");
            
            var sourcePath = $"{agent.rsyncUser}@{agent.hostname}:{backupPlan.source}";
            
            rsyncArgs.Append($"{sourcePath} {backupPlan.destination}");

            var fullCommand = $"rsync {rsyncArgs}";
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("Rsync command: {Command}", fullCommand);
            _logger.LogInformation("Starting rsync execution for backup plan {BackupPlanId} (Simulation: {IsSimulation})", backupPlan.id, isSimulation);

            // Create backup execution and log entries
            Guid executionId = Guid.NewGuid();
            using (var logScope = _serviceScopeFactory.CreateScope())
            {
                var logContext = logScope.ServiceProvider.GetRequiredService<LogDbContext>();
                
                // Create BackupExecution
                var backupExecution = new BackupExecution
                {
                    id = executionId,
                    backupPlanId = backupPlan.id,
                    name = $"{backupPlan.name} - {(isSimulation ? "Simulation" : "Execution")}",
                    startDateTime = startTime
                };
                logContext.BackupExecutions.Add(backupExecution);

                // Log rsync start
                var startLogEntry = new LogEntry
                {
                    id = Guid.NewGuid(),
                    backupPlanId = backupPlan.id,
                    executionId = executionId,
                    datetime = startTime,
                    fileName = "rsync-start",
                    filePath = "",
                    action = LogEntry.Action.System.ToString(),
                    reason = $"Starting rsync execution (Simulation: {isSimulation})"
                };
                logContext.LogEntries.Add(startLogEntry);

                // Log rsync command
                var commandLogEntry = new LogEntry
                {
                    id = Guid.NewGuid(),
                    backupPlanId = backupPlan.id,
                    executionId = executionId,
                    datetime = startTime,
                    fileName = "rsync-command",
                    filePath = fullCommand,
                    action = LogEntry.Action.System.ToString(),
                    reason = "Rsync command executed"
                };
                logContext.LogEntries.Add(commandLogEntry);

                await logContext.SaveChangesAsync();
            }

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
            var logEntriesBatch = new List<LogEntry>();
            var lastSaveTime = DateTime.UtcNow;
            var batchLock = new object();
            const int BatchSaveIntervalSeconds = 2; // Save logs every 2 seconds
            const int MaxBatchSize = 50; // Save when batch reaches 50 entries

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    var line = e.Data;
                    outputBuilder.AppendLine(line);
                    _logger.LogDebug("Rsync output: {Output}", line);

                    // Parse and log file operations in real-time
                    var logEntry = ParseRsyncLine(line, backupPlan.id, executionId, DateTime.UtcNow);
                    if (logEntry != null)
                    {
                        List<LogEntry>? batchToSave = null;
                        
                        lock (batchLock)
                        {
                            logEntriesBatch.Add(logEntry);
                            
                            var now = DateTime.UtcNow;
                            if (logEntriesBatch.Count >= MaxBatchSize || 
                                (now - lastSaveTime).TotalSeconds >= BatchSaveIntervalSeconds)
                            {
                                batchToSave = new List<LogEntry>(logEntriesBatch);
                                logEntriesBatch.Clear();
                                lastSaveTime = now;
                            }
                        }
                        
                        // Update BackupExecution with current file being processed
                        if (!string.IsNullOrEmpty(logEntry.filePath))
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    using var logScope = _serviceScopeFactory.CreateScope();
                                    var logContext = logScope.ServiceProvider.GetRequiredService<LogDbContext>();
                                    
                                    var backupExecution = await logContext.BackupExecutions.FindAsync(executionId);
                                    if (backupExecution != null)
                                    {
                                        backupExecution.currentFileName = logEntry.fileName;
                                        backupExecution.currentFilePath = logEntry.filePath;
                                        await logContext.SaveChangesAsync();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to update BackupExecution current file");
                                }
                            });
                        }

                        // Save batch if it's time or batch is full
                        if (batchToSave != null && batchToSave.Count > 0)
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    using var logScope = _serviceScopeFactory.CreateScope();
                                    var logContext = logScope.ServiceProvider.GetRequiredService<LogDbContext>();
                                    logContext.LogEntries.AddRange(batchToSave);
                                    await logContext.SaveChangesAsync();
                                    _logger.LogDebug("Saved {Count} log entries to database", batchToSave.Count);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to save log entries batch: {Error}", ex.Message);
                                }
                            });
                        }
                    }
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

            var endTime = DateTime.UtcNow;
            var duration = endTime - startTime;
            var output = outputBuilder.ToString();
            var error = errorBuilder.ToString();

            // Save any remaining log entries in the batch
            List<LogEntry> finalBatch;
            lock (batchLock)
            {
                finalBatch = new List<LogEntry>(logEntriesBatch);
                logEntriesBatch.Clear();
            }
            
            if (finalBatch.Count > 0)
            {
                using var logScope = _serviceScopeFactory.CreateScope();
                var logContext = logScope.ServiceProvider.GetRequiredService<LogDbContext>();
                logContext.LogEntries.AddRange(finalBatch);
                await logContext.SaveChangesAsync();
                _logger.LogDebug("Saved final batch of {Count} log entries to database", finalBatch.Count);
            }

            // Log rsync finish to database
            using (var logScope = _serviceScopeFactory.CreateScope())
            {
                var logContext = logScope.ServiceProvider.GetRequiredService<LogDbContext>();
                
                // Update BackupExecution
                var backupExecution = await logContext.BackupExecutions.FindAsync(executionId);
                if (backupExecution != null)
                {
                    backupExecution.endDateTime = endTime;
                }

                // Log rsync finish
                var finishDescription = process.ExitCode == 0
                    ? $"Rsync finished successfully. Exit code: {process.ExitCode}, Duration: {duration.TotalMilliseconds}ms"
                    : $"Rsync finished with failure. Exit code: {process.ExitCode}, Duration: {duration.TotalMilliseconds}ms, Error: {error}";

                var finishLogEntry = new LogEntry
                {
                    id = Guid.NewGuid(),
                    backupPlanId = backupPlan.id,
                    executionId = executionId,
                    datetime = endTime,
                    fileName = "rsync-finish",
                    filePath = "",
                    action = process.ExitCode == 0 ? LogEntry.Action.System.ToString() : LogEntry.Action.CopyError.ToString(),
                    reason = finishDescription
                };
                logContext.LogEntries.Add(finishLogEntry);

                await logContext.SaveChangesAsync();
            }

            if (process.ExitCode != 0)
            {
                _logger.LogError("Rsync finished with failure. Exit code: {ExitCode}, Duration: {Duration}ms, Error: {Error}", 
                    process.ExitCode, duration.TotalMilliseconds, error);
                throw new Exception($"Rsync failed: {error}");
            }

            _logger.LogInformation("Rsync finished successfully. Exit code: {ExitCode}, Duration: {Duration}ms", 
                process.ExitCode, duration.TotalMilliseconds);

            // Parse statistics from rsync output
            ParseRsyncStatistics(output, result, duration.TotalSeconds);

            // Parse output for simulation mode
            if (isSimulation)
            {
                ParseRsyncOutput(output, result);
            }
            else
            {
                _logger.LogInformation("Backup completed successfully for backup plan {BackupPlanId}", backupPlan.id);
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

    private LogEntry? ParseRsyncLine(string line, Guid backupPlanId, Guid executionId, DateTime timestamp)
    {
        var trimmedLine = line.Trim();
        
        // Skip empty lines, progress lines, and stats lines
        if (string.IsNullOrWhiteSpace(trimmedLine) ||
            trimmedLine.StartsWith("receiving incremental file list") ||
            trimmedLine.StartsWith("Number of files:") ||
            trimmedLine.StartsWith("Number of created files:") ||
            trimmedLine.StartsWith("Number of deleted files:") ||
            trimmedLine.StartsWith("Number of regular files transferred:") ||
            trimmedLine.StartsWith("Total file size:") ||
            trimmedLine.StartsWith("Total transferred file size:") ||
            trimmedLine.StartsWith("Literal data:") ||
            trimmedLine.StartsWith("Matched data:") ||
            trimmedLine.StartsWith("File list size:") ||
            trimmedLine.StartsWith("File list generation time:") ||
            trimmedLine.StartsWith("File list transfer time:") ||
            trimmedLine.StartsWith("Total bytes sent:") ||
            trimmedLine.StartsWith("Total bytes received:") ||
            (trimmedLine.StartsWith("sent") && trimmedLine.Contains("bytes/sec")) ||
            trimmedLine.StartsWith("total size is") ||
            trimmedLine.Contains("%") && trimmedLine.Contains("kB/s") ||
            trimmedLine.StartsWith("Warning:") ||
            trimmedLine.StartsWith("Building file list"))
        {
            return null;
        }
        
        // Parse deletion lines (e.g., "*deleting   license.rtf")
        if (trimmedLine.StartsWith("*deleting"))
        {
            var match = Regex.Match(trimmedLine, @"^\*deleting\s+(.+)$");
            if (match.Success)
            {
                var filePath = match.Groups[1].Value.Trim();
                var fileName = Path.GetFileName(filePath);
                
                return new LogEntry
                {
                    id = Guid.NewGuid(),
                    backupPlanId = backupPlanId,
                    executionId = executionId,
                    datetime = timestamp,
                    fileName = fileName,
                    filePath = filePath,
                    action = LogEntry.Action.Delete.ToString(),
                    reason = "File deleted by rsync"
                };
            }
        }
        
        // Parse itemize-changes lines (e.g., ">f+++++++++ wefwef" or ".d..t...... ./")
        if (trimmedLine.StartsWith(">") || trimmedLine.StartsWith("<") || trimmedLine.StartsWith("."))
        {
            // Skip lines that are just directory timestamp updates (e.g., ".d..t...... ./")
            if (trimmedLine.StartsWith(".") && (trimmedLine.Contains("./") || trimmedLine.TrimEnd() == "."))
            {
                return null;
            }

            // Match pattern: >f+++++++++ filepath or <f+++++++++ filepath or .d..t...... filepath
            var match = Regex.Match(trimmedLine, @"^([<>.])([fdLDS])([.+\-<>chstT]+)\s+(.+)$");
            if (match.Success)
            {
                var direction = match.Groups[1].Value; // '>' receiving, '<' sending, '.' update
                var itemType = match.Groups[2].Value; // 'f' file, 'd' directory, 'L' symlink, etc.
                var flags = match.Groups[3].Value;
                var filePath = match.Groups[4].Value.Trim();
                
                // Skip if path is just "./" or "." or empty
                if (string.IsNullOrWhiteSpace(filePath) || filePath == "./" || filePath == ".")
                {
                    return null;
                }
                
                var fileName = Path.GetFileName(filePath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = filePath;
                }

                // Determine action based on flags
                LogEntry.Action action;
                string reason;

                if (flags.Contains("+++++++++") || flags.Contains("+++++"))
                {
                    // New file/directory
                    action = LogEntry.Action.Copy;
                    reason = itemType == "d" ? "New directory created" : "New file copied";
                }
                else if (flags.Contains(">"))
                {
                    // Size changed
                    action = LogEntry.Action.Copy;
                    reason = "File size changed";
                }
                else if (flags.Contains("c"))
                {
                    // Checksum changed
                    action = LogEntry.Action.Copy;
                    reason = "File checksum changed";
                }
                else if (flags.Contains("t") || flags.Contains("T"))
                {
                    // Timestamp changed
                    action = LogEntry.Action.Copy;
                    reason = "File timestamp updated";
                }
                else if (flags.Contains("h"))
                {
                    // Hard link
                    action = LogEntry.Action.Copy;
                    reason = "Hard link created";
                }
                else if (flags.Contains("s"))
                {
                    // Size changed
                    action = LogEntry.Action.Copy;
                    reason = "File size changed";
                }
                else
                {
                    // Other changes
                    action = LogEntry.Action.Copy;
                    reason = "File updated";
                }

                return new LogEntry
                {
                    id = Guid.NewGuid(),
                    backupPlanId = backupPlanId,
                    executionId = executionId,
                    datetime = timestamp,
                    fileName = fileName,
                    filePath = filePath,
                    action = action.ToString(),
                    reason = reason
                };
            }
        }

        return null;
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
            // Parse deletion lines
            else if (line.Trim().StartsWith("*deleting"))
            {
                var match = Regex.Match(line.Trim(), @"^\*deleting\s+(.+)$");
                if (match.Success)
                {
                    var path = match.Groups[1].Value.Trim();
                    var item = new ExecutionItems
                    {
                        FilePath = path,
                        FileName = Path.GetFileName(path),
                        Action = "Delete",
                        Reason = "File deleted"
                    };
                    items.Add(item);
                }
            }
        }

        result.Items = items;
        result.TotalItems = items.Count;
        result.ItemsToCopy = items.Count(i => i.Action == "Copy");
        result.ItemsToDelete = items.Count(i => i.Action == "Delete");
    }

    private void ParseRsyncStatistics(string output, ExecutionResult result, double durationSeconds)
    {
        result.DurationSeconds = durationSeconds;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Number of files: 201 (reg: 163, dir: 38)
            if (trimmedLine.StartsWith("Number of files:"))
            {
                var match = Regex.Match(trimmedLine, @"Number of files:\s+([\d.]+)\s+\(reg:\s+([\d.]+),\s+dir:\s+([\d.]+)\)");
                if (match.Success)
                {
                    result.TotalFiles = ParseNumber(match.Groups[1].Value);
                    result.RegularFiles = ParseNumber(match.Groups[2].Value);
                    result.Directories = ParseNumber(match.Groups[3].Value);
                }
            }
            // Number of created files: 1 (reg: 1)
            else if (trimmedLine.StartsWith("Number of created files:"))
            {
                var match = Regex.Match(trimmedLine, @"Number of created files:\s+([\d.]+)\s+\(reg:\s+([\d.]+)\)");
                if (match.Success)
                {
                    result.CreatedFiles = ParseNumber(match.Groups[1].Value);
                }
            }
            // Number of deleted files: 1 (reg: 1)
            else if (trimmedLine.StartsWith("Number of deleted files:"))
            {
                var match = Regex.Match(trimmedLine, @"Number of deleted files:\s+([\d.]+)\s+\(reg:\s+([\d.]+)\)");
                if (match.Success)
                {
                    result.DeletedFiles = ParseNumber(match.Groups[1].Value);
                }
            }
            // Number of regular files transferred: 1
            else if (trimmedLine.StartsWith("Number of regular files transferred:"))
            {
                var match = Regex.Match(trimmedLine, @"Number of regular files transferred:\s+([\d.]+)");
                if (match.Success)
                {
                    result.TransferredFiles = ParseNumber(match.Groups[1].Value);
                }
            }
            // Total file size: 42.076.393 bytes
            else if (trimmedLine.StartsWith("Total file size:"))
            {
                var match = Regex.Match(trimmedLine, @"Total file size:\s+([\d.]+)\s+bytes");
                if (match.Success)
                {
                    result.TotalFileSize = ParseBytes(match.Groups[1].Value);
                }
            }
            // Total transferred file size: 0 bytes
            else if (trimmedLine.StartsWith("Total transferred file size:"))
            {
                var match = Regex.Match(trimmedLine, @"Total transferred file size:\s+([\d.]+)\s+bytes");
                if (match.Success)
                {
                    result.TotalTransferredSize = ParseBytes(match.Groups[1].Value);
                }
            }
            // Literal data: 0 bytes
            else if (trimmedLine.StartsWith("Literal data:"))
            {
                var match = Regex.Match(trimmedLine, @"Literal data:\s+([\d.]+)\s+bytes");
                if (match.Success)
                {
                    result.LiteralData = ParseBytes(match.Groups[1].Value);
                }
            }
            // Matched data: 0 bytes
            else if (trimmedLine.StartsWith("Matched data:"))
            {
                var match = Regex.Match(trimmedLine, @"Matched data:\s+([\d.]+)\s+bytes");
                if (match.Success)
                {
                    result.MatchedData = ParseBytes(match.Groups[1].Value);
                }
            }
            // File list size: 4.529
            else if (trimmedLine.StartsWith("File list size:"))
            {
                var match = Regex.Match(trimmedLine, @"File list size:\s+([\d.]+)");
                if (match.Success)
                {
                    result.FileListSize = ParseBytes(match.Groups[1].Value);
                }
            }
            // File list generation time: 0,001 seconds
            else if (trimmedLine.StartsWith("File list generation time:"))
            {
                var match = Regex.Match(trimmedLine, @"File list generation time:\s+([\d,]+)\s+seconds");
                if (match.Success)
                {
                    result.FileListGenerationTime = ParseDecimal(match.Groups[1].Value);
                }
            }
            // File list transfer time: 0,000 seconds
            else if (trimmedLine.StartsWith("File list transfer time:"))
            {
                var match = Regex.Match(trimmedLine, @"File list transfer time:\s+([\d,]+)\s+seconds");
                if (match.Success)
                {
                    result.FileListTransferTime = ParseDecimal(match.Groups[1].Value);
                }
            }
            // Total bytes sent: 90
            else if (trimmedLine.StartsWith("Total bytes sent:"))
            {
                var match = Regex.Match(trimmedLine, @"Total bytes sent:\s+([\d.]+)");
                if (match.Success)
                {
                    result.TotalBytesSent = ParseBytes(match.Groups[1].Value);
                }
            }
            // Total bytes received: 4.620
            else if (trimmedLine.StartsWith("Total bytes received:"))
            {
                var match = Regex.Match(trimmedLine, @"Total bytes received:\s+([\d.]+)");
                if (match.Success)
                {
                    result.TotalBytesReceived = ParseBytes(match.Groups[1].Value);
                }
            }
            // sent 90 bytes  received 4.620 bytes  9.420,00 bytes/sec
            else if (trimmedLine.StartsWith("sent") && trimmedLine.Contains("bytes/sec"))
            {
                var match = Regex.Match(trimmedLine, @"sent\s+[\d.]+\s+bytes\s+received\s+[\d.]+\s+bytes\s+([\d.,]+)\s+bytes/sec");
                if (match.Success)
                {
                    result.TransferSpeedBytesPerSecond = ParseDecimal(match.Groups[1].Value);
                }
            }
            // total size is 42.076.393  speedup is 8.933,42
            else if (trimmedLine.StartsWith("total size is") && trimmedLine.Contains("speedup is"))
            {
                var match = Regex.Match(trimmedLine, @"total size is\s+[\d.]+\s+speedup is\s+([\d.,]+)");
                if (match.Success)
                {
                    result.Speedup = ParseDecimal(match.Groups[1].Value);
                }
            }
        }

        // Calculate average speed if not already parsed from output and duration is available
        if (result.TransferSpeedBytesPerSecond == 0 && durationSeconds > 0 && result.TotalTransferredSize > 0)
        {
            result.TransferSpeedBytesPerSecond = result.TotalTransferredSize / durationSeconds;
        }
    }

    private int ParseNumber(string value)
    {
        // Remove thousand separators (periods) and parse
        var cleaned = value.Replace(".", "");
        if (int.TryParse(cleaned, out var result))
        {
            return result;
        }
        return 0;
    }

    private long ParseBytes(string value)
    {
        // Remove thousand separators (periods) and parse
        var cleaned = value.Replace(".", "");
        if (long.TryParse(cleaned, out var result))
        {
            return result;
        }
        return 0;
    }

    private double ParseDecimal(string value)
    {
        // Replace comma with dot for decimal separator and remove thousand separators
        var cleaned = value.Replace(".", "").Replace(",", ".");
        if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }
        return 0;
    }

    public async Task<ExecutionResult> SimulateBackupPlanAsync(BackupPlan backupPlan)
    {
        return await ExecuteBackupPlanAsync(backupPlan, false, true);
    }




}
