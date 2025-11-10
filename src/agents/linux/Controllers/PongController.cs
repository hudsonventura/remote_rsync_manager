using Microsoft.AspNetCore.Mvc;

namespace server.Controllers;

[ApiController]
[Route("[controller]")]
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
        return Ok();
    }
}
