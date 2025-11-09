using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using server.Data;
using server.Models;

namespace server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BackupPlanController : ControllerBase
{
    private readonly DBContext _context;
    private readonly ILogger<BackupPlanController> _logger;

    public BackupPlanController(DBContext context, ILogger<BackupPlanController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost]
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

        // Check if agent exists
        var agent = await _context.Agents.FindAsync(request.AgentId);
        if (agent == null)
        {
            return NotFound(new { message = "Agent not found" });
        }

        var backupPlan = new BackupPlan
        {
            id = Guid.NewGuid(),
            name = request.Name.Trim(),
            description = request.Description?.Trim() ?? string.Empty,
            schedule = string.IsNullOrWhiteSpace(request.Schedule) ? "0 0 * * *" : request.Schedule.Trim(),
            source = request.Source.Trim(),
            destination = request.Destination.Trim(),
            agent = agent
        };

        try
        {
            _context.BackupPlans.Add(backupPlan);
            await _context.SaveChangesAsync();
            
            // Verify the agentid was set
            var savedAgentId = EF.Property<Guid?>(backupPlan, "agentid");
            _logger.LogInformation("Backup plan saved with AgentId: {AgentId}", savedAgentId);

            _logger.LogInformation("Backup plan created with ID: {BackupPlanId}, Name: {Name}, Agent: {AgentId}", 
                backupPlan.id, backupPlan.name, request.AgentId);

            return CreatedAtAction(
                nameof(GetBackupPlansByAgent),
                new { agentId = request.AgentId },
                backupPlan);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating backup plan with name: {Name}", request.Name);
            return StatusCode(500, new { message = "An error occurred while creating the backup plan" });
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<BackupPlanResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllBackupPlans()
    {
        try
        {
            // Query backup plans with agent information using a join
            var query = from bp in _context.BackupPlans
                       join a in _context.Agents on EF.Property<Guid?>(bp, "agentid") equals a.id into agentGroup
                       from agent in agentGroup.DefaultIfEmpty()
                       select new BackupPlanResponse
                       {
                           Id = bp.id,
                           Name = bp.name,
                           Description = bp.description,
                           Schedule = bp.schedule,
                           Source = bp.source,
                           Destination = bp.destination,
                           AgentId = EF.Property<Guid?>(bp, "agentid"),
                           AgentHostname = agent != null ? agent.hostname : null
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

    [HttpGet("{id}")]
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

    [HttpPut("{id}")]
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

    [HttpGet("agent/{agentId}")]
    [ProducesResponseType(typeof(List<BackupPlan>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBackupPlansByAgent(Guid agentId)
    {
        try
        {
            // Check if agent exists
            var agentExists = await _context.Agents.AnyAsync(a => a.id == agentId);
            if (!agentExists)
            {
                return NotFound(new { message = "Agent not found" });
            }

            // Get backup plans for the agent
            // Query by agentid column (as per migration)
            var backupPlans = await _context.BackupPlans
                .Where(bp => EF.Property<Guid?>(bp, "agentid") == agentId)
                .ToListAsync();

            return Ok(backupPlans);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving backup plans for agent {AgentId}", agentId);
            return StatusCode(500, new { message = "An error occurred while retrieving backup plans" });
        }
    }
}

