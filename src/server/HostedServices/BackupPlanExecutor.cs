using System.Security.Cryptography.X509Certificates;
using server.Data;
using server.Models;
using server.Services;

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

    public async Task ExecuteBackupPlanAsync(BackupPlan backupPlan, Agent agent, bool isAutomatic = true)
    {
        Guid executionId = Guid.Empty;
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();

            _logger.LogInformation("Executing backup plan {BackupPlanId} for agent {AgentHostname}", backupPlan.id, agent.hostname);

            // Get log context for logging operations
            var logContext = scope.ServiceProvider.GetRequiredService<LogDbContext>();

            // Create backup execution record
            var startDateTime = DateTime.UtcNow;
            var executionType = isAutomatic ? "Automatic" : "Manual";
            var executionName = $"{startDateTime:yyyy/MM/dd HH:mm} - {executionType} - {backupPlan.name}";
            var execution = new BackupExecution
            {
                id = Guid.NewGuid(),
                backupPlanId = backupPlan.id,
                name = executionName,
                startDateTime = startDateTime
            };
            executionId = execution.id;
            
            logContext.BackupExecutions.Add(execution);
            await logContext.SaveChangesAsync();

            _logger.LogInformation("Created backup execution {ExecutionId} for backup plan {BackupPlanId}", executionId, backupPlan.id);

            // Reload agent from database to ensure we have the latest token
            var agentFromDb = await dbContext.Agents.FindAsync(agent.id);
            if (agentFromDb == null)
            {
                _logger.LogError("Agent {AgentId} not found in database", agent.id);
                throw new InvalidOperationException($"Agent {agent.id} not found");
            }

            // Check if agent has a token
            if (string.IsNullOrEmpty(agentFromDb.token))
            {
                _logger.LogError("Agent {AgentHostname} does not have a token. Cannot execute backup plan {BackupPlanId}",
                    agentFromDb.hostname, backupPlan.id);
                throw new InvalidOperationException($"Agent {agentFromDb.hostname} is not authenticated. Please pair the agent first.");
            }

            // Log: Source analysis started
            await LogMilestoneEvent(logContext, backupPlan.id, executionId, "Analysis Started", "Started analyzing source file structure");

            // Call the /Look endpoint to get file system items from source (remote agent)
            var sourceFileSystemItems = await CallLookEndpointAsync(agentFromDb, backupPlan.source);

            _logger.LogInformation("Retrieved {Count} file system items from agent {AgentHostname} for path {Source}",
                sourceFileSystemItems.Count, agentFromDb.hostname, backupPlan.source);

            // Get file system items from local destination
            var destinationFileSystemItems = GetLocalFileSystemItems(backupPlan.destination);

            _logger.LogInformation("Retrieved {Count} file system items from local destination {Destination}",
                destinationFileSystemItems.Count, backupPlan.destination);

            // Compare source and destination to determine what to copy and delete
            var comparisonResult = CompareFileSystemItems(
                sourceFileSystemItems,
                destinationFileSystemItems,
                backupPlan.source,
                backupPlan.destination);

            _logger.LogInformation(
                "Comparison complete: {CopyCount} items to copy, {DeleteCount} items to delete",
                comparisonResult.NewItems.Count,
                comparisonResult.DeletedItems.Count);

            // Delete files from destination that don't exist in source
            await DeleteFilesFromDestination(comparisonResult.DeletedItems, backupPlan.destination, backupPlan.id, executionId, logContext);

            // Log: Copies started
            await LogMilestoneEvent(logContext, backupPlan.id, executionId, "Copies Started", "Started copying files from source to destination");

            // Copy files from source (agent) to destination
            await foreach (var copiedFile in CopyFilesFromSource(comparisonResult.NewItems, agentFromDb, backupPlan.source, backupPlan.destination, backupPlan.id, executionId, logContext, "Does not exist on destination"))
            {
                _logger.LogInformation("Copied file: {SourcePath} -> {DestinationPath}", copiedFile.SourcePath, copiedFile.DestinationPath);
            }

            await foreach (var copiedFile in CopyFilesFromSource(comparisonResult.EditedItems, agentFromDb, backupPlan.source, backupPlan.destination, backupPlan.id, executionId, logContext, "Changed on source"))
            {
                _logger.LogInformation("Copied file: {SourcePath} -> {DestinationPath}", copiedFile.SourcePath, copiedFile.DestinationPath);
            }

            // Log: Copies finished
            await LogMilestoneEvent(logContext, backupPlan.id, executionId, "Copies Finished", "Finished copying files from source to destination");

            // Log files that were ignored (exist in both with same size)
            await LogIgnoredFiles(sourceFileSystemItems, destinationFileSystemItems, backupPlan.id, executionId, logContext);

            // Update execution end time and clear current file
            execution.endDateTime = DateTime.UtcNow;
            execution.currentFileName = null;
            execution.currentFilePath = null;
            await logContext.SaveChangesAsync();

            _logger.LogInformation("Backup plan {BackupPlanId} execution completed successfully", backupPlan.id);

            // Create notification
            try
            {
                var notificationService = scope.ServiceProvider.GetService<INotificationService>();
                if (notificationService != null)
                {
                    await notificationService.CreateBackupCompletedNotificationAsync(
                        backupPlan.id, 
                        executionId, 
                        backupPlan.name, 
                        true);
                }
            }
            catch (Exception notifEx)
            {
                _logger.LogWarning(notifEx, "Failed to create notification for backup plan {BackupPlanId}", backupPlan.id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing backup plan {BackupPlanId}", backupPlan.id);
            
            // Update execution end time even on error
            if (executionId != Guid.Empty)
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var logContext = scope.ServiceProvider.GetRequiredService<LogDbContext>();
                    var execution = await logContext.BackupExecutions.FindAsync(executionId);
                    if (execution != null)
                    {
                        execution.endDateTime = DateTime.UtcNow;
                        await logContext.SaveChangesAsync();
                    }

                    // Create failure notification
                    try
                    {
                        var notificationService = scope.ServiceProvider.GetService<INotificationService>();
                        if (notificationService != null)
                        {
                            await notificationService.CreateBackupCompletedNotificationAsync(
                                backupPlan.id, 
                                executionId, 
                                backupPlan.name, 
                                false, 
                                ex.Message);
                        }
                    }
                    catch (Exception notifEx)
                    {
                        _logger.LogWarning(notifEx, "Failed to create failure notification for backup plan {BackupPlanId}", backupPlan.id);
                    }
                }
                catch (Exception updateEx)
                {
                    _logger.LogWarning(updateEx, "Failed to update execution end time for {ExecutionId}", executionId);
                }
            }
            
            throw;
        }
    }

    private async Task<List<FileSystemItem>> CallLookEndpointAsync(Agent agent, string sourcePath)
    {
        // Determine base URL - try HTTPS first, then HTTP
        string baseUrl = string.Empty;
        var hostname = agent.hostname;

        if (hostname.StartsWith("http://"))
        {
            baseUrl = hostname;
            hostname = hostname.Substring(7);
        }
        else if (hostname.StartsWith("https://"))
        {
            baseUrl = hostname;
            hostname = hostname.Substring(8);
        }
        else
        {
            // Try HTTPS first, then HTTP as fallback
            string[] protocolsToTry = new[] { "https://", "http://" };
            var lastErrorMessage = string.Empty;
            foreach (var protocol in protocolsToTry)
            {
                var testUrl = $"{protocol}{hostname}/Look?dir={Uri.EscapeDataString(sourcePath)}";
                var result = await TryCallLookEndpointAsync(testUrl, agent.token!);
                if (result.Success && result.Items != null)
                {
                    baseUrl = protocol + hostname;
                    break;
                }
                lastErrorMessage = result.ErrorMessage;
            }
            if (string.IsNullOrEmpty(baseUrl))
            {
                throw new HttpRequestException($"Failed 3 to connect to agent at {agent.hostname}, trying to get https://{hostname}/Look?dir={Uri.EscapeDataString(sourcePath)}. Error: {lastErrorMessage}");
            }
        }

        // Recursively get all files and directories by calling /Look for each directory level
        var allItems = new List<FileSystemItem>();
        await GetDirectoryRecursivelyAsync(baseUrl, agent.token!, sourcePath, allItems);

        return allItems;
    }

    private async Task GetDirectoryRecursivelyAsync(string baseUrl, string agentToken, string directoryPath, List<FileSystemItem> allItems)
    {
        var lookUrl = $"{baseUrl}/Look?dir={Uri.EscapeDataString(directoryPath)}";
        var response = await TryCallLookEndpointAsync(lookUrl, agentToken);

        if (!response.Success)
        {
            _logger.LogWarning("Failed to call /Look endpoint for {Path}: {Error}", directoryPath, response.ErrorMessage);
            return;
        }

        if (response.Items == null || response.Items.Count == 0)
        {
            return;
        }

        // Process items from current directory
        var directoriesToProcess = new List<string>();
        
        foreach (var item in response.Items)
        {
            // Add all items (files and directories) to the result
            allItems.Add(item);

            // If it's a directory, add it to the list to process recursively
            if (item.Type == "directory")
            {
                directoriesToProcess.Add(item.Path);
            }
        }

        // Recursively process subdirectories
        foreach (var subDir in directoriesToProcess)
        {
            await GetDirectoryRecursivelyAsync(baseUrl, agentToken, subDir, allItems);
        }
    }

    private async Task<(bool Success, List<FileSystemItem>? Items, string ErrorMessage)> TryCallLookEndpointAsync(string url, string agentToken)
    {
        // Configure HttpClient to accept self-signed certificates and invalid certificates
        var httpClientHandler = new HttpClientHandler();


        // Accept self-signed certificates and invalid certificates
        httpClientHandler.ServerCertificateCustomValidationCallback =
            (HttpRequestMessage message, X509Certificate2? certificate, X509Chain? chain, System.Net.Security.SslPolicyErrors sslPolicyErrors) =>
            {
                return true;
            };

        using var httpClient = new HttpClient(httpClientHandler);

        httpClient.Timeout = TimeSpan.FromSeconds(30); // Timeout for individual directory listing (non-recursive, should be fast)

        // Add the authentication token header
        httpClient.DefaultRequestHeaders.Add("X-Agent-Token", agentToken);

        try
        {
            _logger.LogInformation("Calling /Look endpoint at {Url}", url);

            var response = await httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var items = await response.Content.ReadFromJsonAsync<List<FileSystemItem>>();
                _logger.LogInformation("Successfully retrieved {Count} items from /Look endpoint", items?.Count ?? 0);
                return (true, items ?? new List<FileSystemItem>(), string.Empty);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Authentication failed at {Url}: {StatusCode}, {Error}",
                    url, response.StatusCode, errorContent);
                return (false, null, "Authentication failed: Invalid or expired token");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to call /Look endpoint at {Url}: {StatusCode}, {Error}",
                    url, response.StatusCode, errorContent);
                return (false, null, $"HTTP {response.StatusCode}: {errorContent}");
            }
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout calling /Look endpoint at {Url}", url);
            return (false, null, "Request timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling /Look endpoint at {Url}", url);
            return (false, null, ex.Message);
        }
    }

    private List<FileSystemItem> GetLocalFileSystemItems(string destinationPath)
    {
        var items = new List<FileSystemItem>();

        try
        {
            // Security check: prevent directory traversal attacks
            if (destinationPath.Contains(".."))
            {
                _logger.LogWarning("Potentially unsafe destination path: {Path}", destinationPath);
                throw new ArgumentException("Invalid destination path: directory traversal (..) is not allowed");
            }

            // Check if directory exists, create if it doesn't
            if (!Directory.Exists(destinationPath))
            {
                _logger.LogInformation("Destination directory does not exist, creating: {Path}", destinationPath);
                Directory.CreateDirectory(destinationPath);
            }

            // Get directory info
            var directoryInfo = new DirectoryInfo(destinationPath);

            // Recursively get all directories and files
            GetAllDirectoriesAndFiles(directoryInfo, items);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied to destination directory: {Path}", destinationPath);
            throw new UnauthorizedAccessException($"Access denied to destination directory: {destinationPath}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading destination directory: {Path}", destinationPath);
            throw;
        }

        // Sort: directories first, then files, both alphabetically
        return items
            .OrderBy(i => i.Type == "file") // Directories first (false < true)
            .ThenBy(i => i.Name)
            .ToList();
    }

    private void GetAllDirectoriesAndFiles(DirectoryInfo directory, List<FileSystemItem> items)
    {
        // Get immediate subdirectories
        DirectoryInfo[] subdirectories;
        try
        {
            subdirectories = directory.GetDirectories();
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Access denied to directory: {Path}", directory.FullName);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting subdirectories from: {Path}", directory.FullName);
            return;
        }

        foreach (var dirInfo in subdirectories)
        {
            try
            {
                items.Add(new FileSystemItem
                {
                    Name = dirInfo.Name,
                    Path = dirInfo.FullName,
                    Type = "directory",
                    Size = null,
                    LastModified = dirInfo.LastWriteTimeUtc,
                    Permissions = GetUnixPermissions(dirInfo.FullName)
                });

                // Recursively process subdirectories
                GetAllDirectoriesAndFiles(dirInfo, items);
            }
            catch (Exception ex)
            {
                // Log but continue processing other directories
                _logger.LogWarning(ex, "Error processing subdirectory: {Path}", dirInfo.FullName);
            }
        }

        // Get files in current directory
        FileInfo[] files;
        try
        {
            files = directory.GetFiles();
        }
        catch (UnauthorizedAccessException)
        {
            // Can't read files, but we already processed subdirectories, so just return
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting files from: {Path}", directory.FullName);
            return;
        }

        foreach (var fileInfo in files)
        {
            try
            {
                items.Add(new FileSystemItem
                {
                    Name = fileInfo.Name,
                    Path = fileInfo.FullName,
                    Type = "file",
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    Permissions = GetUnixPermissions(fileInfo.FullName),
                    Md5 = null // Don't calculate MD5 for browsing (too slow)
                });
            }
            catch (Exception ex)
            {
                // Log but continue processing other files
                _logger.LogWarning(ex, "Error processing file: {Path}", fileInfo.FullName);
            }
        }
    }

    private string? GetUnixPermissions(string path)
    {
        try
        {
            // On Unix-like systems, get file permissions using stat command
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return GetUnixPermissionsViaStat(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting Unix permissions for: {Path}", path);
        }
        return null;
    }

    private string? GetUnixPermissionsViaStat(string path)
    {
        try
        {
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "stat",
                Arguments = $"-c %a \"{path}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processStartInfo);
            if (process == null)
                return null;

            process.WaitForExit(1000); // 1 second timeout

            if (process.ExitCode == 0)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                // stat returns 4 digits sometimes (e.g., "0755"), we want 3 digits
                if (output.Length >= 3)
                {
                    return output.Substring(output.Length - 3);
                }
                return output;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting Unix permissions via stat for: {Path}", path);
        }
        return null;
    }

    private class FileSystemComparisonResult
    {
        public List<FileSystemItem> NewItems { get; set; } = new();
        public List<FileSystemItem> EditedItems { get; set; } = new();
        public List<FileSystemItem> DeletedItems { get; set; } = new();


    }

    private FileSystemComparisonResult CompareFileSystemItems(
        List<FileSystemItem> sourceItems,
        List<FileSystemItem> destinationItems,
        string sourceBasePath,
        string destinationBasePath)
    {
        var result = new FileSystemComparisonResult();

        // Normalize base paths for comparison
        var normalizedSourceBase = NormalizePath(sourceBasePath);
        var normalizedDestBase = NormalizePath(destinationBasePath);

        // Check if source and destination are the same (case-insensitive on Windows)
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (normalizedSourceBase.Equals(normalizedDestBase, comparison))
        {
            _logger.LogWarning("Source and destination paths are the same: {Path}. Skipping backup comparison.", normalizedSourceBase);
            return result; // Return empty result - nothing to copy or delete
        }

        // Create dictionaries for quick lookup by relative path
        // Use case-insensitive comparison on Windows
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var sourceFilesByRelativePath = new Dictionary<string, FileSystemItem>(pathComparer);
        var destinationFilesByRelativePath = new Dictionary<string, FileSystemItem>(pathComparer);


        result.NewItems = sourceItems.Where(s => !destinationItems.Any(d => d.Name == s.Name)).ToList();
        result.DeletedItems = destinationItems.Where(d => !sourceItems.Any(s => s.Name == d.Name)).ToList();
        result.EditedItems = sourceItems.Where(s => destinationItems.Any(d => d.Name == s.Name && d.Size != s.Size)).ToList();

        //dest   "1476 Lisa Manuel - O Raptor da Meia-Noite (Julia Hist 1476).doc"
        //source "1476 Lisa Manuel - O Raptor da Meia-Noite (Julia Hist 1476).doc"


        return result;
    }

    private string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        // Normalize path separators and remove trailing separators
        var normalized = path.Replace('\\', '/').TrimEnd('/');

        // On Windows, preserve drive letter format (C:)
        if (OperatingSystem.IsWindows() && normalized.Length >= 2 && normalized[1] == ':')
        {
            normalized = normalized.Replace('/', '\\');
        }

        return normalized;
    }

    private string GetRelativePath(string fullPath, string basePath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(basePath))
            return string.Empty;

        var normalizedFull = NormalizePath(fullPath);
        var normalizedBase = NormalizePath(basePath);

        // Handle case-insensitive comparison on Windows
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!normalizedFull.StartsWith(normalizedBase, comparison))
        {
            // Path is not under base path - return just the filename
            return Path.GetFileName(normalizedFull);
        }

        // Extract relative path
        var relativePath = normalizedFull.Substring(normalizedBase.Length).TrimStart('/', '\\');

        // Normalize path separators to forward slashes for consistency
        return relativePath.Replace('\\', '/');
    }

    private async Task DeleteFilesFromDestination(List<FileSystemItem> itemsToDelete, string destinationBasePath, Guid backupPlanId, Guid executionId, LogDbContext logContext)
    {
        if (itemsToDelete.Count == 0)
        {
            _logger.LogInformation("No files to delete from destination");
            return;
        }

        _logger.LogInformation("Deleting {Count} files from destination", itemsToDelete.Count);

        int deletedCount = 0;
        int errorCount = 0;

        foreach (var item in itemsToDelete)
        {
            // Update current file being processed
            try
            {
                var execution = await logContext.BackupExecutions.FindAsync(executionId);
                if (execution != null)
                {
                    execution.currentFileName = Path.GetFileName(item.Path);
                    execution.currentFilePath = item.Path;
                    await logContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update current file for execution {ExecutionId}", executionId);
            }

            try
            {
                if (item.Type == "file" && System.IO.File.Exists(item.Path))
                {
                    System.IO.File.Delete(item.Path);
                    deletedCount++;
                    _logger.LogDebug("Deleted file: {Path}", item.Path);

                    // Log the deletion
                    await LogFileOperation(logContext, backupPlanId, executionId, item, "Delete", "Does not exist on source");
                }
                else if (item.Type == "file")
                {
                    _logger.LogWarning("File does not exist, skipping deletion: {Path}", item.Path);
                    // Log as ignored since file doesn't exist
                    await LogFileOperation(logContext, backupPlanId, executionId, item, "Ignored", "File does not exist, cannot delete");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                errorCount++;
                _logger.LogWarning(ex, "Access denied when deleting file: {Path}", item.Path);
                if (item.Type == "file")
                {
                    await LogFileOperation(logContext, backupPlanId, executionId, item, "Ignored", $"Access denied: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogError(ex, "Error deleting file: {Path}", item.Path);
                if (item.Type == "file")
                {
                    await LogFileOperation(logContext, backupPlanId, executionId, item, "Ignored", $"Error: {ex.Message}");
                }
            }
        }

        _logger.LogInformation("Deletion complete: {DeletedCount} deleted, {ErrorCount} errors", deletedCount, errorCount);
    }

    private class CopiedFileInfo
    {
        public string SourcePath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
    }

    private async IAsyncEnumerable<CopiedFileInfo> CopyFilesFromSource(
        List<FileSystemItem> itemsToCopy,
        Agent agent,
        string sourceBasePath,
        string destinationBasePath,
        Guid backupPlanId,
        Guid executionId,
        LogDbContext logContext,
        string reason)
    {
        if (itemsToCopy.Count == 0)
        {
            _logger.LogInformation("No files to copy from source");
            yield break;
        }

        _logger.LogInformation("Copying {Count} files from source to destination", itemsToCopy.Count);

        int copiedCount = 0;
        int errorCount = 0;

        foreach (var sourceItem in itemsToCopy)
        {
            // Only process files, not directories
            if (sourceItem.Type != "file")
            {
                continue;
            }

            // Update current file being processed
            try
            {
                var execution = await logContext.BackupExecutions.FindAsync(executionId);
                if (execution != null)
                {
                    execution.currentFileName = Path.GetFileName(sourceItem.Path);
                    execution.currentFilePath = sourceItem.Path;
                    await logContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update current file for execution {ExecutionId}", executionId);
            }

            // Calculate destination path
            var relativePath = GetRelativePath(sourceItem.Path, NormalizePath(sourceBasePath));
            var destinationPath = Path.Combine(destinationBasePath, relativePath.Replace('/', Path.DirectorySeparatorChar));

            CopiedFileInfo? copiedFile = null;
            try
            {
                // Ensure destination directory exists
                var destinationDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                    _logger.LogDebug("Created destination directory: {Path}", destinationDir);
                }

                // Download file from agent
                await DownloadAndSaveFile(agent, sourceItem.Path, destinationPath);

                copiedCount++;
                copiedFile = new CopiedFileInfo
                {
                    SourcePath = sourceItem.Path,
                    DestinationPath = destinationPath
                };

                // Log the successful copy
                await LogFileOperation(logContext, backupPlanId, executionId, sourceItem, "Copy", reason);
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogError(ex, "Error copying file: {Path}", sourceItem.Path);
                // Log the failed copy as ignored
                await LogFileOperation(logContext, backupPlanId, executionId, sourceItem, "Ignored", $"Error copying: {ex.Message}");
            }

            // Yield the file info outside the try-catch so it can be logged immediately
            if (copiedFile != null)
            {
                yield return copiedFile;
            }
        }

        _logger.LogInformation("Copy complete: {CopiedCount} copied, {ErrorCount} errors", copiedCount, errorCount);
    }

    private async Task DownloadAndSaveFile(Agent agent, string sourceFilePath, string destinationFilePath)
    {
        // Determine base URL
        string baseUrl;
        var hostname = agent.hostname;

        if (hostname.StartsWith("http://"))
        {
            baseUrl = hostname;
            hostname = hostname.Substring(7);
        }
        else if (hostname.StartsWith("https://"))
        {
            baseUrl = hostname;
            hostname = hostname.Substring(8);
        }
        else
        {
            // Try HTTPS first, then HTTP as fallback
            string[] protocolsToTry = new[] { "https://", "http://" };
            foreach (var protocol in protocolsToTry)
            {
                var testUrl = $"{protocol}{hostname}/Download?filePath={Uri.EscapeDataString(sourceFilePath)}";
                var result = await TryDownloadFileAsync(testUrl, agent.token!, destinationFilePath);
                if (result.Success)
                {
                    return;
                }
            }
            throw new HttpRequestException($"Failed 4 to connect to agent at {agent.hostname} to download file");
        }

        var downloadUrl = $"{baseUrl}/Download?filePath={Uri.EscapeDataString(sourceFilePath)}";
        var response = await TryDownloadFileAsync(downloadUrl, agent.token!, destinationFilePath);

        if (!response.Success)
        {
            throw new HttpRequestException($"Failed to download file: {response.ErrorMessage}");
        }
    }

    private async Task<(bool Success, string ErrorMessage)> TryDownloadFileAsync(string url, string agentToken, string destinationPath)
    {
        // Configure HttpClient to accept self-signed certificates and invalid certificates
        var httpClientHandler = new HttpClientHandler();

        httpClientHandler.ServerCertificateCustomValidationCallback =
            (HttpRequestMessage message, X509Certificate2? certificate, X509Chain? chain, System.Net.Security.SslPolicyErrors sslPolicyErrors) =>
            {
                return true;
            };

        using var httpClient = new HttpClient(httpClientHandler);
        httpClient.Timeout = TimeSpan.FromMinutes(10); // Longer timeout for file downloads

        // Add the authentication token header
        httpClient.DefaultRequestHeaders.Add("X-Agent-Token", agentToken);

        try
        {
            _logger.LogInformation("Downloading file from: {Url}", url);

            var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (response.IsSuccessStatusCode)
            {
                // Ensure destination directory exists
                var destinationDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                // Stream the file to disk
                using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var httpStream = await response.Content.ReadAsStreamAsync())
                {
                    await httpStream.CopyToAsync(fileStream);
                }

                _logger.LogInformation("Successfully downloaded and saved file: {DestinationPath}", destinationPath);
                return (true, string.Empty);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Authentication failed at {Url}: {StatusCode}, {Error}",
                    url, response.StatusCode, errorContent);
                return (false, "Authentication failed: Invalid or expired token");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to download file from {Url}: {StatusCode}, {Error}",
                    url, response.StatusCode, errorContent);
                return (false, $"HTTP {response.StatusCode}: {errorContent}");
            }
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout downloading file from {Url}", url);
            return (false, "Request timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from {Url}", url);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Simulates a backup plan execution by creating execution logs without actually performing file operations.
    /// This method creates BackupExecution and LogEntry records to show what would happen.
    /// </summary>
    public async Task<SimulationResult> SimulateBackupPlanAsync(BackupPlan backupPlan, Agent agent)
    {
        Guid executionId = Guid.Empty;
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();
            var logContext = scope.ServiceProvider.GetRequiredService<LogDbContext>();

            _logger.LogInformation("Simulating backup plan {BackupPlanId} for agent {AgentHostname}", backupPlan.id, agent.hostname);

            // Create backup execution record for simulation
            var startDateTime = DateTime.UtcNow;
            var executionName = $"{startDateTime:yyyy/MM/dd HH:mm} - Simulation - {backupPlan.name}";
            var execution = new BackupExecution
            {
                id = Guid.NewGuid(),
                backupPlanId = backupPlan.id,
                name = executionName,
                startDateTime = startDateTime
            };
            executionId = execution.id;
            
            logContext.BackupExecutions.Add(execution);
            await logContext.SaveChangesAsync();

            _logger.LogInformation("Created simulation execution {ExecutionId} for backup plan {BackupPlanId}", executionId, backupPlan.id);

            // Reload agent from database to ensure we have the latest token
            var agentFromDb = await dbContext.Agents.FindAsync(agent.id);
            if (agentFromDb == null)
            {
                _logger.LogError("Agent {AgentId} not found in database", agent.id);
                throw new InvalidOperationException($"Agent {agent.id} not found");
            }

            // Check if agent has a token
            if (string.IsNullOrEmpty(agentFromDb.token))
            {
                _logger.LogError("Agent {AgentHostname} does not have a token. Cannot simulate backup plan {BackupPlanId}",
                    agentFromDb.hostname, backupPlan.id);
                throw new InvalidOperationException($"Agent {agentFromDb.hostname} is not authenticated. Please pair the agent first.");
            }

            // Call the /Look endpoint to get file system items from source (remote agent)
            var sourceFileSystemItems = await CallLookEndpointAsync(agentFromDb, backupPlan.source);

            _logger.LogInformation("Retrieved {Count} file system items from agent {AgentHostname} for path {Source}",
                sourceFileSystemItems.Count, agentFromDb.hostname, backupPlan.source);

            // Get file system items from local destination
            var destinationFileSystemItems = GetLocalFileSystemItems(backupPlan.destination);

            _logger.LogInformation("Retrieved {Count} file system items from local destination {Destination}",
                destinationFileSystemItems.Count, backupPlan.destination);

            // Compare source and destination to determine what to copy and delete
            var comparisonResult = CompareFileSystemItems(
                sourceFileSystemItems,
                destinationFileSystemItems,
                backupPlan.source,
                backupPlan.destination);

            // Build simulation result
            var simulationResult = new SimulationResult();
            var allItems = new List<SimulationItem>();

            // Process new items (to be copied) - create log entries
            foreach (var item in comparisonResult.NewItems)
            {
                if (item.Type == "file") // Only show files, not directories
                {
                    await LogFileOperation(logContext, backupPlan.id, executionId, item, "Copy", "Does not exist on destination");
                    allItems.Add(new SimulationItem
                    {
                        FileName = item.Name,
                        FilePath = item.Path,
                        Size = item.Size,
                        Action = "Copy",
                        Reason = "Does not exist on destination"
                    });
                }
            }

            // Process edited items (to be copied) - create log entries
            foreach (var item in comparisonResult.EditedItems)
            {
                if (item.Type == "file") // Only show files, not directories
                {
                    var destItem = destinationFileSystemItems.FirstOrDefault(d => d.Name == item.Name);
                    var reason = $"Changed on source (size: {destItem?.Size ?? 0} -> {item.Size})";
                    await LogFileOperation(logContext, backupPlan.id, executionId, item, "Copy", reason);
                    allItems.Add(new SimulationItem
                    {
                        FileName = item.Name,
                        FilePath = item.Path,
                        Size = item.Size,
                        Action = "Copy",
                        Reason = reason
                    });
                }
            }

            // Process deleted items (to be deleted) - create log entries
            foreach (var item in comparisonResult.DeletedItems)
            {
                if (item.Type == "file") // Only show files, not directories
                {
                    await LogFileOperation(logContext, backupPlan.id, executionId, item, "Delete", "Does not exist on source");
                    allItems.Add(new SimulationItem
                    {
                        FileName = item.Name,
                        FilePath = item.Path,
                        Size = item.Size,
                        Action = "Delete",
                        Reason = "Does not exist on source"
                    });
                }
            }

            // Log ignored files (files that exist in both with same size)
            await LogIgnoredFiles(sourceFileSystemItems, destinationFileSystemItems, backupPlan.id, executionId, logContext);

            // Sort by action (Copy first, then Delete), then by filename
            allItems = allItems
                .OrderBy(i => i.Action == "Delete") // Copy items first (false < true)
                .ThenBy(i => i.FileName)
                .ToList();

            simulationResult.Items = allItems;
            simulationResult.TotalItems = allItems.Count;
            simulationResult.ItemsToCopy = allItems.Count(i => i.Action == "Copy");
            simulationResult.ItemsToDelete = allItems.Count(i => i.Action == "Delete");

            // Update execution end time
            execution.endDateTime = DateTime.UtcNow;
            await logContext.SaveChangesAsync();

            _logger.LogInformation(
                "Simulation complete: {TotalCount} items, {CopyCount} to copy, {DeleteCount} to delete",
                simulationResult.TotalItems,
                simulationResult.ItemsToCopy,
                simulationResult.ItemsToDelete);

            // Create notification
            try
            {
                var notificationService = scope.ServiceProvider.GetService<INotificationService>();
                if (notificationService != null)
                {
                    await notificationService.CreateSimulationCompletedNotificationAsync(
                        backupPlan.id,
                        executionId,
                        backupPlan.name,
                        simulationResult.TotalItems,
                        simulationResult.ItemsToCopy,
                        simulationResult.ItemsToDelete);
                }
            }
            catch (Exception notifEx)
            {
                _logger.LogWarning(notifEx, "Failed to create notification for simulation of backup plan {BackupPlanId}", backupPlan.id);
            }

            return simulationResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simulating backup plan {BackupPlanId}", backupPlan.id);
            
            // Update execution end time even on error
            if (executionId != Guid.Empty)
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var logContext = scope.ServiceProvider.GetRequiredService<LogDbContext>();
                    var execution = await logContext.BackupExecutions.FindAsync(executionId);
                    if (execution != null)
                    {
                        execution.endDateTime = DateTime.UtcNow;
                        await logContext.SaveChangesAsync();
                    }
                }
                catch (Exception updateEx)
                {
                    _logger.LogWarning(updateEx, "Failed to update simulation execution end time for {ExecutionId}", executionId);
                }
            }
            
            throw;
        }
    }

    private async Task LogFileOperation(LogDbContext logContext, Guid backupPlanId, Guid executionId, FileSystemItem item, string action, string reason)
    {
        try
        {
            var logEntry = new LogEntry
            {
                id = Guid.NewGuid(),
                backupPlanId = backupPlanId,
                executionId = executionId,
                datetime = DateTime.UtcNow,
                fileName = item.Name,
                filePath = item.Path,
                size = item.Size,
                action = action,
                reason = reason
            };

            logContext.LogEntries.Add(logEntry);
            await logContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log file operation for {FileName}", item.Name);
        }
    }

    private async Task LogMilestoneEvent(LogDbContext logContext, Guid backupPlanId, Guid executionId, string eventType, string description)
    {
        try
        {
            var logEntry = new LogEntry
            {
                id = Guid.NewGuid(),
                backupPlanId = backupPlanId,
                executionId = executionId,
                datetime = DateTime.UtcNow,
                fileName = "[System Event]",
                filePath = "",
                size = null,
                action = eventType,
                reason = description
            };

            logContext.LogEntries.Add(logEntry);
            await logContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log milestone event: {EventType}", eventType);
        }
    }

    private async Task LogIgnoredFiles(
        List<FileSystemItem> sourceItems,
        List<FileSystemItem> destinationItems,
        Guid backupPlanId,
        Guid executionId,
        LogDbContext logContext)
    {
        try
        {
            // Find files that exist in both source and destination with the same size (ignored)
            var sourceFiles = sourceItems.Where(s => s.Type == "file").ToList();
            var destinationFiles = destinationItems.Where(d => d.Type == "file").ToList();

            var ignoredFiles = sourceFiles
                .Where(s => destinationFiles.Any(d => d.Name == s.Name && d.Size == s.Size))
                .ToList();

            foreach (var file in ignoredFiles)
            {
                await LogFileOperation(logContext, backupPlanId, executionId, file, "Ignored", "File exists in both source and destination with same size");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log ignored files");
        }
    }
}
