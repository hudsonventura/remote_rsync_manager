using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using server.Data;
using server.Models;

namespace server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentController : ControllerBase
{
    private readonly DBContext _context;
    private readonly ILogger<AgentController> _logger;

    public AgentController(DBContext context, ILogger<AgentController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(Agent), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAgent([FromBody] CreateAgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Hostname))
        {
            return BadRequest(new { message = "Hostname is required" });
        }

        var agent = new Agent
        {
            id = Guid.NewGuid(),
            hostname = request.Hostname.Trim()
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
            _logger.LogError(ex, "Error creating agent with hostname: {Hostname}", request.Hostname);
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

            // Update hostname
            agent.hostname = request.Hostname.Trim();

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
}

