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

    public async Task ExecuteBackupPlanAsync(BackupPlan backupPlan, bool isAutomatic = true)
    {
        Guid executionId = Guid.Empty;
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();

            _logger.LogInformation("Executing backup plan {BackupPlanId}", backupPlan.id);

            // Get rsync configuration from Agent if available, otherwise from BackupPlan
            var rsyncConfig = GetRsyncConfig(backupPlan);
            
            // Validate rsync configuration
            if (string.IsNullOrWhiteSpace(rsyncConfig.Host))
            {
                throw new InvalidOperationException("Backup plan does not have rsync host configured. Please configure rsync connection details in the backup plan or associated agent.");
            }

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

            // Log: Source analysis started
            await LogMilestoneEvent(logContext, backupPlan.id, executionId, "Analysis Started", "Started analyzing source file structure");

            // Get file system items from source using rsync
            var sourceFileSystemItems = await GetRsyncFileSystemItemsAsync(backupPlan, rsyncConfig);

            _logger.LogInformation("Retrieved {Count} file system items from rsync source {Host} for path {Source}",
                sourceFileSystemItems.Count, backupPlan.rsyncHost, backupPlan.source);

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

            // Use rsync to copy files from source to destination
            await ExecuteRsyncBackupAsync(backupPlan, rsyncConfig, executionId, logContext);

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

    private class RsyncConfig
    {
        public string Host { get; set; } = string.Empty;
        public string? User { get; set; }
        public int Port { get; set; } = 22;
        public string? SshKeyContent { get; set; } // SSH private key content (will be written to temp file)
    }

    private RsyncConfig GetRsyncConfig(BackupPlan backupPlan)
    {
        // If backup plan has an agent with rsync config, use that; otherwise use backup plan's config
        if (backupPlan.agent != null && !string.IsNullOrWhiteSpace(backupPlan.agent.hostname))
        {
            return new RsyncConfig
            {
                Host = backupPlan.agent.hostname,
                User = backupPlan.agent.rsyncUser,
                Port = backupPlan.agent.rsyncPort,
                SshKeyContent = backupPlan.agent.rsyncSshKey
            };
        }
        
        return new RsyncConfig
        {
            Host = backupPlan.rsyncHost ?? string.Empty,
            User = backupPlan.rsyncUser,
            Port = backupPlan.rsyncPort,
            SshKeyContent = backupPlan.rsyncSshKey
        };
    }

    private string? CreateTempSshKeyFile(RsyncConfig rsyncConfig)
    {
        if (string.IsNullOrWhiteSpace(rsyncConfig.SshKeyContent))
        {
            return null;
        }

        try
        {
            // Create a temporary file for the SSH key
            var tempDir = Path.Combine(Path.GetTempPath(), "remember_rsync_keys");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            var tempKeyFile = Path.Combine(tempDir, $"ssh_key_{Guid.NewGuid()}");
            File.WriteAllText(tempKeyFile, rsyncConfig.SshKeyContent);
            
            // Set proper permissions (600) on Unix-like systems
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try
                {
                    var processStartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"600 \"{tempKeyFile}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var process = System.Diagnostics.Process.Start(processStartInfo);
                    process?.WaitForExit();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set permissions on temporary SSH key file");
                }
            }

            return tempKeyFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create temporary SSH key file");
            throw;
        }
    }

    private async Task<List<FileSystemItem>> GetRsyncFileSystemItemsAsync(BackupPlan backupPlan, RsyncConfig rsyncConfig)
    {
        var allItems = new List<FileSystemItem>();

        // Build rsync command to list files
        var rsyncSource = BuildRsyncSource(rsyncConfig, backupPlan.source);
        var rsyncArgs = $"--list-only --recursive --human-readable --itemize-changes \"{rsyncSource}\"";

        _logger.LogInformation("Executing rsync list command: rsync {Args}", rsyncArgs);

        var processStartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "rsync",
            Arguments = rsyncArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Create temporary SSH key file if SSH key content is provided
        string? tempSshKeyFile = null;
        try
        {
            tempSshKeyFile = CreateTempSshKeyFile(rsyncConfig);

            // Add SSH options if configured
            if (!string.IsNullOrWhiteSpace(tempSshKeyFile))
            {
                processStartInfo.Environment["RSYNC_RSH"] = $"ssh -i \"{tempSshKeyFile}\" -p {rsyncConfig.Port} -o StrictHostKeyChecking=no";
            }
            else
            {
                processStartInfo.Environment["RSYNC_RSH"] = $"ssh -p {rsyncConfig.Port} -o StrictHostKeyChecking=no";
            }

            using var process = System.Diagnostics.Process.Start(processStartInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start rsync process");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError("Rsync list command failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
                throw new InvalidOperationException($"Rsync failed: {error}");
            }

            // Parse rsync output
            ParseRsyncListOutput(output, backupPlan.source, allItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing rsync list command");
            throw;
        }
        finally
        {
            // Clean up temporary SSH key file
            if (!string.IsNullOrWhiteSpace(tempSshKeyFile) && File.Exists(tempSshKeyFile))
            {
                try
                {
                    File.Delete(tempSshKeyFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary SSH key file: {Path}", tempSshKeyFile);
                }
            }
        }

        return allItems;
    }

    private string BuildRsyncSource(RsyncConfig rsyncConfig, string sourcePath)
    {
        var user = string.IsNullOrWhiteSpace(rsyncConfig.User) ? "" : $"{rsyncConfig.User}@";
        return $"{user}{rsyncConfig.Host}:{sourcePath}";
    }

    private void ParseRsyncListOutput(string output, string sourceBasePath, List<FileSystemItem> items)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("total"))
                continue;

            // Parse rsync output format
            // Format: drwxr-xr-x          4,096 2024/01/01 12:00:00 directory/
            // Format: -rw-r--r--          1,234 2024/01/01 12:00:00 file.txt
            
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
                continue;

            var permissions = parts[0];
            var sizeStr = parts[1].Replace(",", "");
            var dateStr = $"{parts[2]} {parts[3]}";
            var name = string.Join(" ", parts.Skip(4));

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var isDirectory = permissions.StartsWith("d") || name.EndsWith("/");
            var fullPath = Path.Combine(sourceBasePath, name.TrimEnd('/')).Replace('\\', '/');

            long? size = null;
            if (!isDirectory && long.TryParse(sizeStr, out var parsedSize))
            {
                size = parsedSize;
            }

            DateTime lastModified = DateTime.UtcNow;
            if (DateTime.TryParse(dateStr, out var parsedDate))
            {
                lastModified = parsedDate;
            }

            items.Add(new FileSystemItem
            {
                Name = Path.GetFileName(name.TrimEnd('/')),
                PathName = fullPath,
                Path = Path.GetDirectoryName(fullPath) ?? sourceBasePath,
                Type = isDirectory ? "directory" : "file",
                Size = size,
                LastModified = lastModified,
                Permissions = permissions.Length >= 10 ? permissions.Substring(1, 9) : null
            });
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
                    PathName = dirInfo.FullName,
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
                    Path = fileInfo.DirectoryName ?? string.Empty,
                    PathName = fileInfo.FullName,
                    Type = "file",
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    Permissions = GetUnixPermissions(fileInfo.FullName),
                    Md5 = CalculateFileMd5(fileInfo.FullName)
                });
            }
            catch (Exception ex)
            {
                // Log but continue processing other files
                _logger.LogWarning(ex, "Error processing file: {Path}", fileInfo.FullName);
            }
        }
    }

    //TODO: Unify the method to calculate the MD5
    private string? CalculateFileMd5(string filePath)
    {
        try
        {
            using var md5 = MD5.Create();
            using var stream = System.IO.File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating MD5 for file: {Path}", filePath);
            return null;
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
        public List<FileSystemItem> TransferredItems { get; set; } = new();

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


        result.NewItems = sourceItems.Where(
            s => !destinationItems.Any(
                d => GetPathDifference(d.PathName, destinationBasePath) + d.Name == GetPathDifference(s.PathName, sourceBasePath) + s.Name))
            .ToList();

        result.DeletedItems = destinationItems.Where(
            d => !sourceItems.Any(
                s => GetPathDifference(s.PathName, sourceBasePath) + s.Name == GetPathDifference(d.PathName, destinationBasePath) + d.Name))
            .ToList();

        result.EditedItems = sourceItems.Where(
            s => destinationItems.Any(
                d => GetPathDifference(s.PathName, sourceBasePath) + s.Name == GetPathDifference(d.PathName, destinationBasePath) + d.Name && d.Size != s.Size))
            .ToList();

        // result.TransferredItems = result.NewItems.Where(n => result.DeletedItems.Any(
        //     d => d.Md5 == n.Md5 && n.PathName == d.PathName || n.Name != d.Name))
        //     .ToList();

        // result.NewItems.RemoveAll(item => result.TransferredItems.Contains(item));
        // result.DeletedItems.RemoveAll(d =>
        //      result.TransferredItems.Any(t => GetPathDifference(t.PathName, sourceBasePath) == GetPathDifference(d.PathName, destinationBasePath)));

        // var teste = result.TransferredItems[1];
        // var teste3 = GetPathDifference(teste.PathName, sourceBasePath);
        // var teste4 = GetPathDifference(result.DeletedItems[0].PathName, destinationBasePath);

        //string id1 = FileId.Get(teste.PathName);


        return result;
    }

    private string GetPathDifference(string fullPath, string basePath)
    {
        return fullPath.Replace(basePath, "").TrimStart('\\', '/');
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

        //first the itens more deep to be able to delete empty directories
        // First delete the items inside the directories and the directory will be the last item to be deleted, and will be empty
        var delete = itemsToDelete.OrderByDescending(i => i.PathName.Length).ToList();

        foreach (var item in delete)
        {
            // Update current file being processed
            try
            {
                var execution = await logContext.BackupExecutions.FindAsync(executionId);
                if (execution != null)
                {
                    execution.currentFileName = Path.GetFileName(item.PathName);
                    execution.currentFilePath = item.PathName;
                    await logContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update current file for execution {ExecutionId}", executionId);
            }

            try
            {
                if (item.Type == "file" && System.IO.File.Exists(item.PathName))
                {
                    System.IO.File.Delete(item.PathName);
                    deletedCount++;
                    _logger.LogDebug("Deleted file: {Path}", item.PathName);

                    // Log the deletion
                    await LogFileOperation(logContext, backupPlanId, executionId, item, "Delete", "Does not exist on source");
                }
                else if (item.Type == "file")
                {
                    _logger.LogWarning("File does not exist, skipping deletion: {Path}", item.PathName);
                    // Log as ignored since file doesn't exist
                    await LogFileOperation(logContext, backupPlanId, executionId, item, "Ignored", "File does not exist, cannot delete");
                }
                else if (item.Type == "directory" && Directory.Exists(item.PathName))
                {
                    Directory.Delete(item.PathName, true);
                    deletedCount++;
                    _logger.LogDebug("Deleted directory: {Path}", item.PathName);

                    // Log the deletion
                    await LogFileOperation(logContext, backupPlanId, executionId, item, "Delete", "Does not exist on source");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                errorCount++;
                _logger.LogWarning(ex, "Access denied when deleting file: {Path}", item.PathName);
                if (item.Type == "file")
                {
                    await LogFileOperation(logContext, backupPlanId, executionId, item, "Ignored", $"Access denied: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogError(ex, "Error deleting file: {Path}", item.PathName);
                if (item.Type == "file")
                {
                    await LogFileOperation(logContext, backupPlanId, executionId, item, "Ignored", $"Error: {ex.Message}");
                }
            }
        }

        _logger.LogInformation("Deletion complete: {DeletedCount} deleted, {ErrorCount} errors", deletedCount, errorCount);
    }


    private async Task ExecuteRsyncBackupAsync(BackupPlan backupPlan, RsyncConfig rsyncConfig, Guid executionId, LogDbContext logContext)
    {
        _logger.LogInformation("Starting rsync backup from {Source} to {Destination}", backupPlan.source, backupPlan.destination);

        // Ensure destination directory exists
        if (!Directory.Exists(backupPlan.destination))
        {
            Directory.CreateDirectory(backupPlan.destination);
            _logger.LogInformation("Created destination directory: {Path}", backupPlan.destination);
        }

        // Build rsync command
        var rsyncSource = BuildRsyncSource(rsyncConfig, backupPlan.source);
        var rsyncArgs = $"--archive --verbose --delete --itemize-changes \"{rsyncSource}\" \"{backupPlan.destination}\"";

        _logger.LogInformation("Executing rsync backup command: rsync {Args}", rsyncArgs);

        var processStartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "rsync",
            Arguments = rsyncArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Create temporary SSH key file if SSH key content is provided
        string? tempSshKeyFile = null;
        try
        {
            tempSshKeyFile = CreateTempSshKeyFile(rsyncConfig);

            // Add SSH options if configured
            if (!string.IsNullOrWhiteSpace(tempSshKeyFile))
            {
                processStartInfo.Environment["RSYNC_RSH"] = $"ssh -i \"{tempSshKeyFile}\" -p {rsyncConfig.Port} -o StrictHostKeyChecking=no";
            }
            else
            {
                processStartInfo.Environment["RSYNC_RSH"] = $"ssh -p {rsyncConfig.Port} -o StrictHostKeyChecking=no";
            }

            using var process = System.Diagnostics.Process.Start(processStartInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start rsync process");
            }

            // Read output line by line to track progress
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Parse rsync output to update current file
                // Format: >f+++++++++ file.txt
                // Format: *deleting   oldfile.txt
                if (line.StartsWith(">f") || line.StartsWith(">d") || line.StartsWith("*deleting"))
                {
                    var fileName = line.Substring(line.LastIndexOf(' ') + 1).Trim();
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        try
                        {
                            var execution = await logContext.BackupExecutions.FindAsync(executionId);
                            if (execution != null)
                            {
                                execution.currentFileName = fileName;
                                execution.currentFilePath = Path.Combine(backupPlan.destination, fileName);
                                await logContext.SaveChangesAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to update current file for execution {ExecutionId}", executionId);
                        }
                    }
                }

                _logger.LogDebug("Rsync output: {Line}", line);
            }

            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError("Rsync backup command failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
                throw new InvalidOperationException($"Rsync failed: {error}");
            }

            _logger.LogInformation("Rsync backup completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing rsync backup command");
            throw;
        }
        finally
        {
            // Clean up temporary SSH key file
            if (!string.IsNullOrWhiteSpace(tempSshKeyFile) && File.Exists(tempSshKeyFile))
            {
                try
                {
                    File.Delete(tempSshKeyFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary SSH key file: {Path}", tempSshKeyFile);
                }
            }
        }
    }

    /// <summary>
    /// Simulates a backup plan execution by creating execution logs without actually performing file operations.
    /// This method creates BackupExecution and LogEntry records to show what would happen.
    /// </summary>
    public async Task<SimulationResult> SimulateBackupPlanAsync(BackupPlan backupPlan)
    {
        Guid executionId = Guid.Empty;
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();
            var logContext = scope.ServiceProvider.GetRequiredService<LogDbContext>();

            _logger.LogInformation("Simulating backup plan {BackupPlanId} using rsync --dry-run", backupPlan.id);

            // Get rsync configuration from Agent if available, otherwise from BackupPlan
            var rsyncConfig = GetRsyncConfig(backupPlan);
            
            // Validate rsync configuration
            if (string.IsNullOrWhiteSpace(rsyncConfig.Host))
            {
                throw new InvalidOperationException("Backup plan does not have rsync host configured. Please configure rsync connection details in the backup plan or associated agent.");
            }

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

            // Run rsync with --dry-run to get what would be transferred
            var simulationItems = await ExecuteRsyncDryRunAsync(backupPlan, rsyncConfig, executionId, logContext);

            // Sort by action (Copy first, then Delete), then by filename
            var sortedItems = simulationItems
                .OrderBy(i => i.Action == "Delete") // Copy items first (false < true)
                .ThenBy(i => i.FileName)
                .ToList();

            // Build simulation result
            var simulationResult = new SimulationResult
            {
                Items = sortedItems,
                TotalItems = sortedItems.Count,
                ItemsToCopy = sortedItems.Count(i => i.Action == "Copy"),
                ItemsToDelete = sortedItems.Count(i => i.Action == "Delete")
            };

            // Update execution end time
            execution.endDateTime = DateTime.UtcNow;
            await logContext.SaveChangesAsync();

            _logger.LogInformation("Simulation complete: {TotalItems} items, {CopyCount} to copy, {DeleteCount} to delete",
                simulationResult.TotalItems, simulationResult.ItemsToCopy, simulationResult.ItemsToDelete);

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

    private async Task<List<SimulationItem>> ExecuteRsyncDryRunAsync(BackupPlan backupPlan, RsyncConfig rsyncConfig, Guid executionId, LogDbContext logContext)
    {
        _logger.LogInformation("Starting rsync dry-run simulation from {Source} to {Destination}", backupPlan.source, backupPlan.destination);

        // Ensure destination directory exists (for rsync to work, even in dry-run)
        if (!Directory.Exists(backupPlan.destination))
        {
            Directory.CreateDirectory(backupPlan.destination);
            _logger.LogInformation("Created destination directory: {Path}", backupPlan.destination);
        }

        // Build rsync command with --dry-run flag
        var rsyncSource = BuildRsyncSource(rsyncConfig, backupPlan.source);
        var rsyncArgs = $"--archive --verbose --delete --itemize-changes --dry-run \"{rsyncSource}\" \"{backupPlan.destination}\"";

        _logger.LogInformation("Executing rsync dry-run command: rsync {Args}", rsyncArgs);

        var processStartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "rsync",
            Arguments = rsyncArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var simulationItems = new List<SimulationItem>();

        // Create temporary SSH key file if SSH key content is provided
        string? tempSshKeyFile = null;
        try
        {
            tempSshKeyFile = CreateTempSshKeyFile(rsyncConfig);

            // Add SSH options if configured
            if (!string.IsNullOrWhiteSpace(tempSshKeyFile))
            {
                processStartInfo.Environment["RSYNC_RSH"] = $"ssh -i \"{tempSshKeyFile}\" -p {rsyncConfig.Port} -o StrictHostKeyChecking=no";
            }
            else
            {
                processStartInfo.Environment["RSYNC_RSH"] = $"ssh -p {rsyncConfig.Port} -o StrictHostKeyChecking=no";
            }

            using var process = System.Diagnostics.Process.Start(processStartInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start rsync process");
            }

            // Read output line by line to parse what would be transferred
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                _logger.LogDebug("Rsync dry-run output: {Line}", line);

                // Parse rsync itemize-changes output
                // Format examples:
                // >f+++++++++ file.txt (new file)
                // >f.st...... file.txt (file with timestamp change)
                // >f.s...... file.txt (file with size change)
                // *deleting   oldfile.txt (file to delete)
                // cd+++++++++ dirname/ (new directory)
                
                var simulationItem = ParseRsyncDryRunOutput(line, backupPlan.source, backupPlan.destination);
                if (simulationItem != null)
                {
                    simulationItems.Add(simulationItem);
                    
                    // Create log entry for this item
                    var fileSystemItem = new FileSystemItem
                    {
                        Name = simulationItem.FileName,
                        PathName = simulationItem.FilePath,
                        Type = simulationItem.Action == "Delete" ? "file" : "file", // Assume file for now
                        Size = simulationItem.Size
                    };
                    
                    await LogFileOperation(logContext, backupPlan.id, executionId, fileSystemItem, simulationItem.Action, simulationItem.Reason);
                }
            }

            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError("Rsync dry-run command failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
                throw new InvalidOperationException($"Rsync dry-run failed: {error}");
            }

            _logger.LogInformation("Rsync dry-run completed successfully. Found {Count} items to transfer", simulationItems.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing rsync dry-run command");
            throw;
        }
        finally
        {
            // Clean up temporary SSH key file
            if (!string.IsNullOrWhiteSpace(tempSshKeyFile) && File.Exists(tempSshKeyFile))
            {
                try
                {
                    File.Delete(tempSshKeyFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary SSH key file: {Path}", tempSshKeyFile);
                }
            }
        }

        return simulationItems;
    }

    private SimulationItem? ParseRsyncDryRunOutput(string line, string sourceBasePath, string destinationBasePath)
    {
        try
        {
            // Skip summary lines and empty lines
            if (string.IsNullOrWhiteSpace(line) || 
                line.StartsWith("sending incremental") || 
                line.StartsWith("total size") ||
                line.StartsWith("sent ") ||
                line.StartsWith("receiving "))
            {
                return null;
            }

            // Parse deletion: *deleting   filename
            if (line.StartsWith("*deleting"))
            {
                var fileName = line.Substring("*deleting".Length).Trim();
                var filePath = Path.Combine(destinationBasePath, fileName).Replace('\\', '/');
                
                return new SimulationItem
                {
                    FileName = Path.GetFileName(fileName),
                    FilePath = filePath,
                    Size = null,
                    Action = "Delete",
                    Reason = "Does not exist on source"
                };
            }

            // Parse itemize-changes format: >f+++++++++ path/to/file
            // Format: [action flags] filename
            // Action flags:
            //   > = receiving (will be copied)
            //   < = sending (will be sent)
            //   c = local change
            //   h = hard link
            //   . = no change
            //   * = message
            // File type:
            //   f = file
            //   d = directory
            //   L = symlink
            // Change flags:
            //   + = item is new
            //   s = size is different
            //   t = timestamp is different
            //   p = permissions are different
            //   o = owner is different
            //   g = group is different
            //   u = checksum is different
            //   . = no change
            
            if (line.Length > 12 && (line[0] == '>' || line[0] == '<' || line[0] == 'c'))
            {
                var flags = line.Substring(0, 12);
                var fileName = line.Substring(12).Trim();
                
                // Skip directories (we only care about files for simulation)
                if (flags[1] == 'd')
                {
                    return null;
                }

                // Only process files
                if (flags[1] == 'f' || flags[1] == 'L')
                {
                    var action = "Copy";
                    var reason = "New file";
                    
                    // Determine reason based on flags
                    if (flags.Contains('+'))
                    {
                        reason = "New file";
                    }
                    else if (flags.Contains('s'))
                    {
                        reason = "Size changed";
                    }
                    else if (flags.Contains('t'))
                    {
                        reason = "Timestamp changed";
                    }
                    else if (flags.Contains('u'))
                    {
                        reason = "Content changed";
                    }
                    else if (flags.Contains('p'))
                    {
                        reason = "Permissions changed";
                    }

                    // Build full path
                    var filePath = fileName;
                    if (!Path.IsPathRooted(fileName))
                    {
                        filePath = Path.Combine(destinationBasePath, fileName).Replace('\\', '/');
                    }

                    return new SimulationItem
                    {
                        FileName = Path.GetFileName(fileName),
                        FilePath = filePath,
                        Size = null, // Size not available in itemize output
                        Action = action,
                        Reason = reason
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing rsync dry-run output line: {Line}", line);
        }

        return null;
    }

    // OLD METHOD - REMOVED: Now using rsync --dry-run for simulation
    // The old method compared file lists manually, but now we use rsync --dry-run
    // which is more accurate and shows exactly what rsync would do

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
                filePath = item.PathName,
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
