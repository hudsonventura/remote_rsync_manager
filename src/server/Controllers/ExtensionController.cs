using System.IO.Compression;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NCrontab;
using server.Data;
using server.Models;
using server.Services;

namespace server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExtensionController : ControllerBase
{
    private readonly DBContext _context;
    private readonly LogDbContext _logContext;
    private readonly ExtensionExecutor _executor;
    private readonly ExtensionProcessTracker _tracker;
    private readonly ILogger<ExtensionController> _logger;

    public ExtensionController(
        DBContext context,
        LogDbContext logContext,
        ExtensionExecutor executor,
        ExtensionProcessTracker tracker,
        ILogger<ExtensionController> logger)
    {
        _context = context;
        _logContext = logContext;
        _executor = executor;
        _tracker = tracker;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllExtensions()
    {
        var extensions = await _context.Extensions
            .OrderByDescending(e => e.createdAt)
            .ToListAsync();

        var now = DateTime.UtcNow;

        var result = extensions.Select(e =>
        {
            DateTime? nextRun = null;
            try
            {
                var schedule = CrontabSchedule.Parse(e.schedule);
                nextRun = schedule.GetNextOccurrence(now);
            }
            catch
            {
                // Ignore schedule parse error for next run display
            }

            return new
            {
                e.id,
                e.name,
                e.description,
                e.command,
                e.schedule,
                e.active,
                e.folderPath,
                e.environmentVariables,
                e.createdAt,
                e.updatedAt,
                e.lastRunAt,
                e.lastExitCode,
                lastStatus = _tracker.IsRunning(e.id) ? "Running" : e.lastStatus,
                isRunning = _tracker.IsRunning(e.id),
                nextRunAt = nextRun
            };
        });

        return Ok(result);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExtensionById(Guid id)
    {
        var extension = await _context.Extensions.FindAsync(id);
        if (extension == null)
        {
            return NotFound(new { message = $"Extension with ID {id} not found" });
        }

        DateTime? nextRun = null;
        try
        {
            var schedule = CrontabSchedule.Parse(extension.schedule);
            nextRun = schedule.GetNextOccurrence(DateTime.UtcNow);
        }
        catch
        {
            // Ignore
        }

        return Ok(new
        {
            extension.id,
            extension.name,
            extension.description,
            extension.command,
            extension.schedule,
            extension.active,
            extension.folderPath,
            extension.environmentVariables,
            extension.createdAt,
            extension.updatedAt,
            extension.lastRunAt,
            extension.lastExitCode,
            lastStatus = _tracker.IsRunning(extension.id) ? "Running" : extension.lastStatus,
            isRunning = _tracker.IsRunning(extension.id),
            nextRunAt = nextRun
        });
    }

    [HttpPost]
    [RequestSizeLimit(104857600)] // 100 MB limit
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateExtension([FromForm] CreateExtensionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.name))
        {
            return BadRequest(new { message = "Extension name is required" });
        }

        if (string.IsNullOrWhiteSpace(request.command))
        {
            return BadRequest(new { message = "Execution command is required (e.g., 'dotnet App.dll')" });
        }

        if (string.IsNullOrWhiteSpace(request.schedule))
        {
            return BadRequest(new { message = "Cron schedule is required" });
        }

        try
        {
            CrontabSchedule.Parse(request.schedule);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Invalid cron expression: {ex.Message}" });
        }

        if (request.zipFile == null || request.zipFile.Length == 0)
        {
            return BadRequest(new { message = "A valid ZIP file containing the .NET application is required" });
        }

        if (!request.zipFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Only .zip archive files are supported" });
        }

        var extensionId = Guid.NewGuid();
        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "data");
        var targetFolder = Path.Combine(dataDirectory, "extensions", extensionId.ToString());

        try
        {
            if (Directory.Exists(targetFolder))
            {
                Directory.Delete(targetFolder, true);
            }
            Directory.CreateDirectory(targetFolder);

            // Safe ZIP extraction with traversal check
            using var stream = request.zipFile.OpenReadStream();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var normalizedTargetFolder = Path.GetFullPath(targetFolder);
            if (!normalizedTargetFolder.EndsWith(Path.DirectorySeparatorChar))
            {
                normalizedTargetFolder += Path.DirectorySeparatorChar;
            }

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) && (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\")))
                {
                    continue;
                }

                var destinationPath = Path.GetFullPath(Path.Combine(targetFolder, entry.FullName));
                if (!destinationPath.StartsWith(normalizedTargetFolder, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Malicious zip entry path detected: {entry.FullName}");
                }

                var dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                entry.ExtractToFile(destinationPath, overwrite: true);
            }

            var extension = new Extension
            {
                id = extensionId,
                name = request.name.Trim(),
                description = request.description?.Trim(),
                command = request.command.Trim(),
                schedule = request.schedule.Trim(),
                active = request.active,
                folderPath = targetFolder,
                environmentVariables = request.environmentVariables,
                createdAt = DateTime.UtcNow,
                updatedAt = DateTime.UtcNow,
                lastStatus = "Never Run"
            };

            _context.Extensions.Add(extension);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Extension '{Name}' ({Id}) created and unpacked in {Folder}",
                extension.name, extension.id, targetFolder);

            return CreatedAtAction(nameof(GetExtensionById), new { id = extension.id }, extension);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create extension: {Message}", ex.Message);

            // Cleanup unpacked files on error
            if (Directory.Exists(targetFolder))
            {
                try { Directory.Delete(targetFolder, true); } catch { }
            }

            return BadRequest(new { message = $"Failed to unpack or save extension: {ex.Message}" });
        }
    }

    [HttpPut("{id}")]
    [RequestSizeLimit(104857600)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateExtension(Guid id, [FromForm] UpdateExtensionRequest request)
    {
        var extension = await _context.Extensions.FindAsync(id);
        if (extension == null)
        {
            return NotFound(new { message = $"Extension with ID {id} not found" });
        }

        if (string.IsNullOrWhiteSpace(request.name))
        {
            return BadRequest(new { message = "Extension name is required" });
        }

        if (string.IsNullOrWhiteSpace(request.command))
        {
            return BadRequest(new { message = "Execution command is required" });
        }

        if (string.IsNullOrWhiteSpace(request.schedule))
        {
            return BadRequest(new { message = "Cron schedule is required" });
        }

        try
        {
            CrontabSchedule.Parse(request.schedule);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Invalid cron expression: {ex.Message}" });
        }

        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "data");
        var targetFolder = !string.IsNullOrWhiteSpace(extension.folderPath)
            ? extension.folderPath
            : Path.Combine(dataDirectory, "extensions", extension.id.ToString());

        // If new ZIP file is supplied, unpack and replace files
        if (request.zipFile != null && request.zipFile.Length > 0)
        {
            if (!request.zipFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Only .zip archive files are supported" });
            }

            try
            {
                if (Directory.Exists(targetFolder))
                {
                    Directory.Delete(targetFolder, true);
                }
                Directory.CreateDirectory(targetFolder);

                using var stream = request.zipFile.OpenReadStream();
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

                var normalizedTargetFolder = Path.GetFullPath(targetFolder);
                if (!normalizedTargetFolder.EndsWith(Path.DirectorySeparatorChar))
                {
                    normalizedTargetFolder += Path.DirectorySeparatorChar;
                }

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name) && (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\")))
                    {
                        continue;
                    }

                    var destinationPath = Path.GetFullPath(Path.Combine(targetFolder, entry.FullName));
                    if (!destinationPath.StartsWith(normalizedTargetFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Malicious zip entry path detected: {entry.FullName}");
                    }

                    var dir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    entry.ExtractToFile(destinationPath, overwrite: true);
                }

                _logger.LogInformation("Extension '{Name}' ({Id}) files updated in {Folder}",
                    extension.name, extension.id, targetFolder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update extension files: {Message}", ex.Message);
                return BadRequest(new { message = $"Failed to unpack replacement ZIP: {ex.Message}" });
            }
        }

        extension.name = request.name.Trim();
        extension.description = request.description?.Trim();
        extension.command = request.command.Trim();
        extension.schedule = request.schedule.Trim();
        extension.active = request.active;
        extension.environmentVariables = request.environmentVariables;
        extension.folderPath = targetFolder;
        extension.updatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(extension);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteExtension(Guid id)
    {
        var extension = await _context.Extensions.FindAsync(id);
        if (extension == null)
        {
            return NotFound(new { message = $"Extension with ID {id} not found" });
        }

        var targetFolder = extension.folderPath;
        if (!string.IsNullOrWhiteSpace(targetFolder) && Directory.Exists(targetFolder))
        {
            try
            {
                Directory.Delete(targetFolder, true);
                _logger.LogInformation("Deleted extension files at {Folder}", targetFolder);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete extension folder {Folder}", targetFolder);
            }
        }

        // Delete execution history from log db
        var executions = await _logContext.ExtensionExecutions
            .Where(e => e.extensionId == id)
            .ToListAsync();

        if (executions.Count > 0)
        {
            _logContext.ExtensionExecutions.RemoveRange(executions);
            await _logContext.SaveChangesAsync();
        }

        _context.Extensions.Remove(extension);
        await _context.SaveChangesAsync();

        return Ok(new { message = $"Extension '{extension.name}' successfully deleted" });
    }

    [HttpPost("{id}/toggle-active")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleActive(Guid id)
    {
        var extension = await _context.Extensions.FindAsync(id);
        if (extension == null)
        {
            return NotFound(new { message = $"Extension with ID {id} not found" });
        }

        extension.active = !extension.active;
        extension.updatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(extension);
    }

    [HttpPost("{id}/execute")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExecuteNow(Guid id)
    {
        var extension = await _context.Extensions.FindAsync(id);
        if (extension == null)
        {
            return NotFound(new { message = $"Extension with ID {id} not found" });
        }

        // Execute in background to return quickly or execute synchronously?
        // Since manual executions might take a few seconds, we can run it asynchronously
        // and return the execution record immediately or execute and wait.
        // Let's execute and return the result!
        try
        {
            var result = await _executor.ExecuteAsync(id, isAutomatic: false);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = $"Failed to execute extension: {ex.Message}" });
        }
    }

    [HttpGet("{id}/executions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExecutions(Guid id, [FromQuery] int limit = 50)
    {
        var executions = await _logContext.ExtensionExecutions
            .Where(e => e.extensionId == id)
            .OrderByDescending(e => e.startDateTime)
            .Take(Math.Min(limit, 100))
            .ToListAsync();

        foreach (var exec in executions)
        {
            var liveOutput = _tracker.GetLiveOutput(exec.id);
            if (liveOutput != null)
            {
                exec.output = liveOutput;
                exec.status = "Running";
            }
        }

        return Ok(executions);
    }

    [HttpGet("{id}/executions/{executionId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExecutionDetails(Guid id, Guid executionId)
    {
        var execution = await _logContext.ExtensionExecutions
            .FirstOrDefaultAsync(e => e.extensionId == id && e.id == executionId);

        if (execution == null)
        {
            return NotFound(new { message = "Execution not found" });
        }

        var liveOutput = _tracker.GetLiveOutput(executionId);
        if (liveOutput != null)
        {
            execution.output = liveOutput;
            execution.status = "Running";
        }

        return Ok(execution);
    }

    [HttpPost("{id}/stop")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StopExtension(Guid id)
    {
        var extension = await _context.Extensions.FindAsync(id);
        if (extension == null)
        {
            return NotFound(new { message = $"Extension with ID {id} not found" });
        }

        var stopped = _tracker.StopExtension(id);
        if (stopped)
        {
            _logger.LogInformation("Process termination requested for extension '{Name}' ({Id})", extension.name, id);
            return Ok(new { message = "Process termination signal sent successfully." });
        }

        return Ok(new { message = "No running process found for this extension." });
    }

    [HttpPost("{id}/executions/{executionId}/stop")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult StopExecution(Guid id, Guid executionId)
    {
        var stopped = _tracker.StopExecution(executionId);
        if (stopped)
        {
            _logger.LogInformation("Process termination requested for execution {ExecutionId}", executionId);
            return Ok(new { message = "Process termination signal sent successfully." });
        }

        return Ok(new { message = "No running process found for this execution." });
    }
}

