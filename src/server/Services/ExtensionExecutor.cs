using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using server.Data;
using server.Models;

namespace server.Services;

public class ExtensionExecutor
{
    private readonly ILogger<ExtensionExecutor> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ExtensionProcessTracker _tracker;

    public ExtensionExecutor(
        ILogger<ExtensionExecutor> logger,
        IServiceScopeFactory serviceScopeFactory,
        ExtensionProcessTracker tracker)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _tracker = tracker;
    }

    public async Task<ExtensionExecution> ExecuteAsync(Guid extensionId, bool isAutomatic = true, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();
        var logDbContext = scope.ServiceProvider.GetRequiredService<LogDbContext>();

        var extension = await dbContext.Extensions.FirstOrDefaultAsync(e => e.id == extensionId, cancellationToken);
        if (extension == null)
        {
            _logger.LogError("Extension with ID {ExtensionId} not found", extensionId);
            throw new KeyNotFoundException($"Extension with ID {extensionId} not found");
        }

        var executionId = Guid.NewGuid();
        var startTime = DateTime.UtcNow;

        var execution = new ExtensionExecution
        {
            id = executionId,
            extensionId = extension.id,
            extensionName = extension.name,
            command = extension.command,
            startDateTime = startTime,
            status = "Running",
            isAutomatic = isAutomatic
        };

        logDbContext.ExtensionExecutions.Add(execution);
        await logDbContext.SaveChangesAsync(cancellationToken);

        extension.lastStatus = "Running";
        extension.lastRunAt = startTime;
        await dbContext.SaveChangesAsync(cancellationToken);

        var outputBuilder = new StringBuilder();
        int exitCode = -1;
        string finalStatus = "Failed";
        Process? process = null;
        int processId = 0;

        try
        {
            var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "data");
            var defaultExtensionFolder = Path.Combine(dataDirectory, "extensions", extension.id.ToString());
            var folder = (!string.IsNullOrWhiteSpace(extension.folderPath) && Directory.Exists(extension.folderPath))
                ? extension.folderPath
                : defaultExtensionFolder;

            if (!Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException($"Extension directory does not exist: {folder}");
            }

            // Determine executable and arguments from command string
            var (fileName, arguments) = ParseCommand(extension.command);

            _logger.LogInformation("Executing extension '{Name}' ({Id}): {FileName} {Arguments} in {WorkingDir}",
                extension.name, extension.id, fileName, arguments, folder);

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = folder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Inherit environment variables
            foreach (var key in Environment.GetEnvironmentVariables().Keys)
            {
                var envKey = key.ToString();
                if (envKey != null && !startInfo.Environment.ContainsKey(envKey))
                {
                    startInfo.Environment[envKey] = Environment.GetEnvironmentVariable(envKey);
                }
            }

            // Fixed built-in environment variables for Remember extensions
            var storePath = ResolveStorePath(dataDirectory);
            var hostPath = ResolveHostPath(dataDirectory);

            startInfo.Environment["STORE_PATH"] = storePath;
            startInfo.Environment["REMEMBER_STORE_PATH"] = storePath;
            startInfo.Environment["HOST_PATH"] = hostPath;
            startInfo.Environment["REMEMBER_HOST_PATH"] = hostPath;
            startInfo.Environment["DATA_PATH"] = dataDirectory;
            startInfo.Environment["REMEMBER_DATA_PATH"] = dataDirectory;
            startInfo.Environment["EXTENSION_DIR"] = folder;
            startInfo.Environment["EXTENSION_PATH"] = folder;
            startInfo.Environment["REMEMBER_EXTENSION_DIR"] = folder;
            startInfo.Environment["EXTENSION_ID"] = extension.id.ToString();
            startInfo.Environment["EXTENSION_NAME"] = extension.name;

            // Inject custom extension environment variables
            var customEnvVars = ParseEnvironmentVariables(extension.environmentVariables);
            if (customEnvVars.Count > 0)
            {
                _logger.LogInformation("Injecting {Count} custom environment variables for extension '{Name}' ({Id}): {Keys}",
                    customEnvVars.Count, extension.name, extension.id, string.Join(", ", customEnvVars.Keys));

                foreach (var (k, v) in customEnvVars)
                {
                    startInfo.Environment[k] = v;
                }
            }

            process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    lock (outputBuilder)
                    {
                        outputBuilder.AppendLine(args.Data);
                    }
                }
            };

            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    lock (outputBuilder)
                    {
                        outputBuilder.AppendLine($"[STDERR] {args.Data}");
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try { processId = process.Id; } catch { }

            // Set a 30-minute timeout for execution
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            _tracker.Register(executionId, extension.id, process, processId, linkedCts, outputBuilder);

            // Periodic database flush task to keep logs in DB reasonably fresh
            using var flushCts = new CancellationTokenSource();
            var flushTask = Task.Run(async () =>
            {
                using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));
                while (!flushCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        if (await periodicTimer.WaitForNextTickAsync(flushCts.Token))
                        {
                            if (process.HasExited) break;
                            using var flushScope = _serviceScopeFactory.CreateScope();
                            var flushDb = flushScope.ServiceProvider.GetRequiredService<LogDbContext>();
                            var activeExec = await flushDb.ExtensionExecutions.FindAsync(executionId);
                            if (activeExec != null)
                            {
                                lock (outputBuilder)
                                {
                                    activeExec.output = outputBuilder.ToString();
                                }
                                await flushDb.SaveChangesAsync(CancellationToken.None);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // Ignore periodic flush errors
                    }
                }
            }, flushCts.Token);

            await process.WaitForExitAsync(linkedCts.Token);
            flushCts.Cancel();
            try { await flushTask; } catch { }

            exitCode = process.ExitCode;
            finalStatus = exitCode == 0 ? "Success" : "Failed";

            _logger.LogInformation("Extension '{Name}' ({Id}) finished with exit code {ExitCode}",
                extension.name, extension.id, exitCode);
        }
        catch (OperationCanceledException)
        {
            finalStatus = "Stopped";
            exitCode = -1;

            ProcessHelper.KillProcessTree(process, processId, _logger);

            lock (outputBuilder)
            {
                outputBuilder.AppendLine("[ABORTED] Execution was cancelled or manually stopped.");
            }
            _logger.LogWarning("Extension '{Name}' ({Id}) was stopped or timed out", extension.name, extension.id);
        }
        catch (Exception ex)
        {
            finalStatus = "Failed";
            exitCode = -1;

            ProcessHelper.KillProcessTree(process, processId, _logger);

            lock (outputBuilder)
            {
                outputBuilder.AppendLine($"[EXCEPTION] {ex.Message}");
                if (ex.StackTrace != null)
                {
                    outputBuilder.AppendLine(ex.StackTrace);
                }
            }
            _logger.LogError(ex, "Error while running extension '{Name}' ({Id})", extension.name, extension.id);
        }
        finally
        {
            if (finalStatus != "Success")
            {
                ProcessHelper.KillProcessTree(process, processId, _logger);
            }

            _tracker.Unregister(executionId);
            process?.Dispose();

            var endTime = DateTime.UtcNow;

            execution.endDateTime = endTime;
            execution.exitCode = exitCode;
            execution.status = finalStatus;
            execution.output = outputBuilder.ToString();

            await logDbContext.SaveChangesAsync(CancellationToken.None);

            extension.lastRunAt = startTime;
            extension.lastExitCode = exitCode;
            extension.lastStatus = finalStatus;
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        return execution;
    }

    private static (string fileName, string arguments) ParseCommand(string command)
    {
        var trimmed = command.Trim();

        if (trimmed.StartsWith("dotnet ", StringComparison.OrdinalIgnoreCase))
        {
            return ("dotnet", trimmed.Substring(7).Trim());
        }

        if (trimmed.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || trimmed.Contains(".dll "))
        {
            return ("dotnet", trimmed);
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return (parts[0], string.Empty);
        }

        return (parts[0], parts[1]);
    }

    public static Dictionary<string, string> ParseEnvironmentVariables(string? json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            // Try deserializing as List of { key, value }
            var list = JsonSerializer.Deserialize<List<EnvironmentVariableDto>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (list != null)
            {
                foreach (var item in list)
                {
                    if (!string.IsNullOrWhiteSpace(item.key))
                    {
                        result[item.key.Trim()] = item.value ?? string.Empty;
                    }
                }
                return result;
            }
        }
        catch
        {
            // Fallback: Try deserializing as Dictionary<string, string>
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Key))
                        {
                            result[kvp.Key.Trim()] = kvp.Value ?? string.Empty;
                        }
                    }
                    return result;
                }
            }
            catch
            {
                // Ignore parse failures
            }
        }

        return result;
    }

    private class EnvironmentVariableDto
    {
        public string key { get; set; } = string.Empty;
        public string value { get; set; } = string.Empty;
        public string? type { get; set; }
        public bool? isSecret { get; set; }
    }

    private static string ResolveStorePath(string dataDirectory)
    {
        var envStore = Environment.GetEnvironmentVariable("STORE_PATH")
            ?? Environment.GetEnvironmentVariable("REMEMBER_STORE_PATH");

        if (!string.IsNullOrWhiteSpace(envStore))
        {
            try
            {
                if (!Directory.Exists(envStore))
                {
                    Directory.CreateDirectory(envStore);
                }
            }
            catch { }
            return envStore;
        }

        if (Directory.Exists("/store"))
        {
            return "/store";
        }

        var localStore = Path.Combine(dataDirectory, "store");
        try
        {
            if (!Directory.Exists(localStore))
            {
                Directory.CreateDirectory(localStore);
            }
        }
        catch { }

        return localStore;
    }

    private static string ResolveHostPath(string dataDirectory)
    {
        var envHost = Environment.GetEnvironmentVariable("HOST_PATH")
            ?? Environment.GetEnvironmentVariable("REMEMBER_HOST_PATH");

        if (!string.IsNullOrWhiteSpace(envHost))
        {
            try
            {
                if (!Directory.Exists(envHost))
                {
                    Directory.CreateDirectory(envHost);
                }
            }
            catch { }
            return envHost;
        }

        if (Directory.Exists("/host"))
        {
            return "/host";
        }

        var localHost = Path.Combine(dataDirectory, "host");
        try
        {
            if (!Directory.Exists(localHost))
            {
                Directory.CreateDirectory(localHost);
            }
        }
        catch { }

        return localHost;
    }
}

