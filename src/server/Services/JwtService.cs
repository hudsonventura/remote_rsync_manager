using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using server.Models;
using server.Data;

namespace server.Services;

public class JwtService : IJwtService
{
    private readonly IConfiguration _configuration;
    private readonly DBContext _dbContext;

    public JwtService(IConfiguration configuration, DBContext dbContext)
    {
        _configuration = configuration;
        _dbContext = dbContext;
    }

    public string GenerateToken(User user)
    {
        // Get JWT configuration from database
        var jwtConfig = _dbContext.JwtConfigs.FirstOrDefault()
            ?? throw new InvalidOperationException("JWT configuration not found in database");

        var secretKey = jwtConfig.secretKey;
        var issuer = jwtConfig.issuer;
        var audience = jwtConfig.audience;

        // ExpirationMinutes can still come from appsettings or default to 60
        var jwtSettings = _configuration.GetSection("Jwt");
        var expirationMinutes = int.Parse(jwtSettings["ExpirationMinutes"] ?? "60");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.id.ToString()),
            new Claim(ClaimTypes.Name, user.username),
            new Claim(ClaimTypes.Email, user.email),
            new Claim(ClaimTypes.Role, user.isAdmin ? "Admin" : "User"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

