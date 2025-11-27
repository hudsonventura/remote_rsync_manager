using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using server.Data;
using server.Models;
using server.HostedServices;

namespace server.Controllers;

[ApiController]
public class BackupPlanController : ControllerBase
{
    private readonly DBContext _context;
    private readonly ILogger<BackupPlanController> _logger;

    public BackupPlanController(DBContext context, ILogger<BackupPlanController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost("/api/backupplan")]
    [ProducesResponseType(typeof(BackupPlan), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateBackupPlan([FromBody] CreateBackupPlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Name is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            return BadRequest(new { message = "Source is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Destination))
        {
            return BadRequest(new { message = "Destination is required" });
        }

        if (string.IsNullOrWhiteSpace(request.RsyncHost))
        {
            return BadRequest(new { message = "Rsync host is required" });
        }

        var backupPlan = new BackupPlan
        {
            id = Guid.NewGuid(),
            name = request.Name.Trim(),
            description = request.Description?.Trim() ?? string.Empty,
            schedule = string.IsNullOrWhiteSpace(request.Schedule) ? "0 0 * * *" : request.Schedule.Trim(),
            source = request.Source.Trim(),
            destination = request.Destination.Trim(),
            active = request.Active,
            rsyncHost = request.RsyncHost.Trim(),
            rsyncUser = request.RsyncUser?.Trim(),
            rsyncPort = request.RsyncPort ?? 22,
            rsyncSshKey = request.RsyncSshKey?.Trim()
        };

        try
        {
            _context.BackupPlans.Add(backupPlan);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Backup plan created with ID: {BackupPlanId}, Name: {Name}, RsyncHost: {RsyncHost}", 
                backupPlan.id, backupPlan.name, backupPlan.rsyncHost);

            return CreatedAtAction(
                nameof(GetBackupPlan),
                new { id = backupPlan.id },
                backupPlan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating backup plan with name: {Name}", request.Name);
            return StatusCode(500, new { message = "An error occurred while creating the backup plan" });
        }
    }

    [HttpGet("/api/backupplan")]
    [ProducesResponseType(typeof(List<BackupPlanResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllBackupPlans()
    {
        try
        {
            // Query backup plans
            var query = from bp in _context.BackupPlans
                       select new BackupPlanResponse
                       {
                           Id = bp.id,
                           Name = bp.name,
                           Description = bp.description,
                           Schedule = bp.schedule,
                           Source = bp.source,
                           Destination = bp.destination,
                           Active = bp.active,
                           AgentId = EF.Property<Guid?>(bp, "agentid"),
                           AgentHostname = bp.rsyncHost
                       };

            var response = await query.ToListAsync();
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all backup plans");
            return StatusCode(500, new { message = "An error occurred while retrieving backup plans" });
        }
    }

    [HttpGet("/api/backupplan/{id}")]
    [ProducesResponseType(typeof(BackupPlan), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBackupPlan(Guid id)
    {
        try
        {
            var backupPlan = await _context.BackupPlans.FindAsync(id);

            if (backupPlan == null)
            {
                return NotFound(new { message = "Backup plan not found" });
            }

            return Ok(backupPlan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving backup plan {BackupPlanId}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving the backup plan" });
        }
    }

    [HttpPut("/api/backupplan/{id}")]
    [ProducesResponseType(typeof(BackupPlan), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateBackupPlan(Guid id, [FromBody] UpdateBackupPlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Name is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            return BadRequest(new { message = "Source is required" });
        }

        if (string.IsNullOrWhiteSpace(request.Destination))
        {
            return BadRequest(new { message = "Destination is required" });
        }

        try
        {
            var backupPlan = await _context.BackupPlans.FindAsync(id);

            if (backupPlan == null)
            {
                return NotFound(new { message = "Backup plan not found" });
            }

            // Update properties
            backupPlan.name = request.Name.Trim();
            backupPlan.description = request.Description?.Trim() ?? string.Empty;
            backupPlan.schedule = string.IsNullOrWhiteSpace(request.Schedule) ? "0 0 * * *" : request.Schedule.Trim();
            backupPlan.source = request.Source.Trim();
            backupPlan.destination = request.Destination.Trim();
            backupPlan.active = request.Active;
            
            // Update rsync properties if provided
            if (!string.IsNullOrWhiteSpace(request.RsyncHost))
            {
                backupPlan.rsyncHost = request.RsyncHost.Trim();
            }
            if (request.RsyncUser != null)
            {
                backupPlan.rsyncUser = request.RsyncUser.Trim();
            }
            if (request.RsyncPort.HasValue)
            {
                backupPlan.rsyncPort = request.RsyncPort.Value;
            }
            if (request.RsyncSshKey != null)
            {
                backupPlan.rsyncSshKey = request.RsyncSshKey.Trim();
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Backup plan updated with ID: {BackupPlanId}, Name: {Name}", 
                backupPlan.id, backupPlan.name);

            return Ok(backupPlan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating backup plan {BackupPlanId}", id);
            return StatusCode(500, new { message = "An error occurred while updating the backup plan" });
        }
    }


    [HttpDelete("/api/backupplan/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBackupPlan(Guid id)
    {
        try
        {
            var backupPlan = await _context.BackupPlans.FindAsync(id);

            if (backupPlan == null)
            {
                return NotFound(new { message = "Backup plan not found" });
            }

            _context.BackupPlans.Remove(backupPlan);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Backup plan deleted with ID: {BackupPlanId}, Name: {Name}", 
                backupPlan.id, backupPlan.name);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting backup plan {BackupPlanId}", id);
            return StatusCode(500, new { message = "An error occurred while deleting the backup plan" });
        }
    }

    [HttpPost("/api/backupplan/{id}/simulate")]
    [ProducesResponseType(typeof(SimulationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SimulateBackupPlan(Guid id)
    {
        try
        {
            var backupPlan = await _context.BackupPlans
                .FirstOrDefaultAsync(bp => bp.id == id);

            if (backupPlan == null)
            {
                return NotFound(new { message = "Backup plan not found" });
            }

            if (string.IsNullOrWhiteSpace(backupPlan.rsyncHost))
            {
                return BadRequest(new { message = "Backup plan does not have rsync host configured" });
            }

            var executor = HttpContext.RequestServices.GetRequiredService<BackupPlanExecutor>();
            var simulationResult = await executor.SimulateBackupPlanAsync(backupPlan);

            return Ok(simulationResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simulating backup plan {BackupPlanId}", id);
            return StatusCode(500, new { message = "An error occurred while simulating the backup plan", error = ex.Message });
        }
    }

    [HttpPost("/api/backupplan/{id}/execute")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExecuteBackupPlan(Guid id)
    {
        try
        {
            var backupPlan = await _context.BackupPlans
                .FirstOrDefaultAsync(bp => bp.id == id);

            if (backupPlan == null)
            {
                return NotFound(new { message = "Backup plan not found" });
            }

            if (string.IsNullOrWhiteSpace(backupPlan.rsyncHost))
            {
                return BadRequest(new { message = "Backup plan does not have rsync host configured" });
            }

            var executor = HttpContext.RequestServices.GetRequiredService<BackupPlanExecutor>();
            
            // Execute asynchronously in the background (manual execution)
            _ = Task.Run(async () =>
            {
                try
                {
                    await executor.ExecuteBackupPlanAsync(backupPlan, false);
                    _logger.LogInformation("Manual execution of backup plan {BackupPlanId} completed successfully", id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during manual execution of backup plan {BackupPlanId}", id);
                }
            });

            return Accepted(new { message = "Backup plan execution started" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting backup plan execution {BackupPlanId}", id);
            return StatusCode(500, new { message = "An error occurred while starting the backup plan execution", error = ex.Message });
        }
    }
}

