using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using server.Data;
using server.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentController : ControllerBase
{
    private readonly DBContext _context;
    private readonly ILogger<AgentController> _logger;
    private readonly IWebHostEnvironment _environment;

    public AgentController(DBContext context, ILogger<AgentController> logger, IWebHostEnvironment environment)
    {
        _context = context;
        _logger = logger;
        _environment = environment;
    }

    [HttpPost]
    [ProducesResponseType(typeof(Agent), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateAgent([FromBody] CreateAgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Hostname))
        {
            return BadRequest(new { message = "Hostname is required" });
        }

        var hostname = request.Hostname.Trim();

        // Create agent with rsync configuration (no token needed for rsync)
        var agent = new Agent
        {
            id = Guid.NewGuid(),
            name = string.IsNullOrWhiteSpace(request.Name) ? "New Agent" : request.Name.Trim(),
            hostname = hostname,
            token = null, // Token not needed for rsync-based agents
            rsyncUser = request.RsyncUser?.Trim(),
            rsyncPort = request.RsyncPort ?? 22,
            rsyncSshKey = request.RsyncSshKey?.Trim()
        };

        try
        {
            _context.Agents.Add(agent);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Agent created with ID: {AgentId}, Hostname: {Hostname}", agent.id, agent.hostname);

            return CreatedAtAction(
                nameof(GetAgent),
                new { id = agent.id },
                agent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating agent with hostname: {Hostname}", hostname);
            return StatusCode(500, new { message = "An error occurred while creating the agent" });
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<Agent>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAgents()
    {
        try
        {
            var agents = await _context.Agents.ToListAsync();
            return Ok(agents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agents");
            return StatusCode(500, new { message = "An error occurred while retrieving agents" });
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Agent), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAgent(Guid id)
    {
        var agent = await _context.Agents.FindAsync(id);

        if (agent == null)
        {
            return NotFound(new { message = "Agent not found" });
        }

        return Ok(agent);
    }

    [HttpPost("{id}/validate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidateAgent(Guid id)
    {
        var agent = await _context.Agents.FindAsync(id);

        if (agent == null)
        {
            return NotFound(new { message = "Agent not found" });
        }

        // For rsync-based agents, validation just checks if configuration is present
        return Ok(new { 
            message = "Agent configuration is valid for rsync connections", 
            hostname = agent.hostname,
            hasSshKey = !string.IsNullOrWhiteSpace(agent.rsyncSshKey),
            rsyncUser = agent.rsyncUser,
            rsyncPort = agent.rsyncPort
        });
    }


    [HttpPut("{id}")]
    [ProducesResponseType(typeof(Agent), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAgent(Guid id, [FromBody] UpdateAgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Hostname))
        {
            return BadRequest(new { message = "Hostname is required" });
        }

        try
        {
            var agent = await _context.Agents.FindAsync(id);

            if (agent == null)
            {
                return NotFound(new { message = "Agent not found" });
            }

            // Update hostname and name
            agent.hostname = request.Hostname.Trim();
            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                agent.name = request.Name.Trim();
            }
            
            // Update rsync connection details if provided
            if (request.RsyncUser != null)
            {
                agent.rsyncUser = request.RsyncUser.Trim();
            }
            if (request.RsyncPort.HasValue)
            {
                agent.rsyncPort = request.RsyncPort.Value;
            }
            if (request.RsyncSshKey != null)
            {
                agent.rsyncSshKey = request.RsyncSshKey.Trim();
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Agent updated with ID: {AgentId}, Hostname: {Hostname}", 
                agent.id, agent.hostname);

            return Ok(agent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating agent {AgentId}", id);
            return StatusCode(500, new { message = "An error occurred while updating the agent" });
        }
    }


    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAgent(Guid id)
    {
        try
        {
            var agent = await _context.Agents.FindAsync(id);

            if (agent == null)
            {
                return NotFound(new { message = "Agent not found" });
            }

            // Delete all backup plans associated with this agent
            var backupPlans = await _context.BackupPlans
                .Where(bp => EF.Property<Guid?>(bp, "agentid") == id)
                .ToListAsync();

            if (backupPlans.Any())
            {
                _context.BackupPlans.RemoveRange(backupPlans);
                _logger.LogInformation("Deleting {Count} backup plans for agent {AgentId}", backupPlans.Count, id);
            }

            // Delete the agent
            _context.Agents.Remove(agent);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Agent deleted with ID: {AgentId}, Hostname: {Hostname}", agent.id, agent.hostname);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent {AgentId}", id);
            return StatusCode(500, new { message = "An error occurred while deleting the agent" });
        }
    }

    public class CreateAgentRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string? RsyncUser { get; set; }
        public int? RsyncPort { get; set; }
        public string? RsyncSshKey { get; set; }
    }

    public class UpdateAgentRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string? RsyncUser { get; set; }
        public int? RsyncPort { get; set; }
        public string? RsyncSshKey { get; set; }
    }
}

