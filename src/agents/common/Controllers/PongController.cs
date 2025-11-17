using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AgentCommon.Controllers;

[ApiController]
public class PongController : ControllerBase
{
    private readonly ILogger<PongController> _logger;

    public PongController(ILogger<PongController> logger)
    {
        _logger = logger;
    }

    [HttpGet("/Pong")]
    public IActionResult Get()
    {
        _logger.LogInformation("Pong endpoint called");
        return Ok(new { message = "Pong", timestamp = DateTime.UtcNow });
    }
}

