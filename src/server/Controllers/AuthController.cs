using Microsoft.AspNetCore.Mvc;
using server.Models;
using server.Services;

namespace server.Controllers;

[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IJwtService _jwtService;
    private readonly ITokenStore _tokenStore;

    public AuthController(IAuthService authService, IJwtService jwtService, ITokenStore tokenStore)
    {
        _authService = authService;
        _jwtService = jwtService;
        _tokenStore = tokenStore;
    }

    [HttpPost("/login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _authService.ValidateUserAsync(request.Email, request.Password);
        
        if (user == null)
        {
            return Unauthorized(new { message = "Invalid email or password" });
        }

        var token = _jwtService.GenerateToken(user);
        var expirationMinutes = int.Parse(
            HttpContext.RequestServices
                .GetRequiredService<IConfiguration>()
                .GetSection("Jwt")["ExpirationMinutes"] ?? "60"
        );

        var expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes);

        // Store the token in the static list
        _tokenStore.StoreToken(token, expiresAt, user.Email);

        var response = new AuthResponse(
            Token: token,
            Email: user.Email,
            ExpiresAt: expiresAt
        );

        return Ok(response);
    }
}

