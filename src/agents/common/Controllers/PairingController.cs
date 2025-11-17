using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AgentCommon.Data;
using AgentCommon.Models;

namespace AgentCommon.Controllers;

[ApiController]
public class PairingController : ControllerBase
{
    private readonly ILogger<PairingController> _logger;
    private readonly AgentDbContext _context;
    private static readonly TimeSpan CodeValidityDuration = TimeSpan.FromMinutes(10);

    public PairingController(ILogger<PairingController> logger, AgentDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    private void GenerateNewCode()
    {
        // Remove expired codes
        var expiredCodes = _context.PairingCodes
            .Where(pc => pc.expires_at <= DateTime.UtcNow)
            .ToList();
        _context.PairingCodes.RemoveRange(expiredCodes);

        // Generate new code
        var random = new Random();
        var code = random.Next(100000, 999999).ToString();
        var expiresAt = DateTime.UtcNow.Add(CodeValidityDuration);

        var pairingCode = new PairingCode
        {
            code = code,
            created_at = DateTime.UtcNow,
            expires_at = expiresAt
        };

        _context.PairingCodes.Add(pairingCode);
        _context.SaveChanges();
        
        _logger.LogInformation("=== PAIRING CODE GENERATED ===");
        _logger.LogInformation("Code: {PairingCode}", code);
        _logger.LogInformation("Valid for 10 minutes");
        _logger.LogInformation("==============================");
        
        Console.WriteLine("\n========================================");
        Console.WriteLine("  PAIRING CODE: " + code);
        Console.WriteLine("  Valid for 10 minutes");
        Console.WriteLine("  Use this code to pair the agent");
        Console.WriteLine("========================================\n");
    }

    [HttpGet("/Pairing/code")]
    public IActionResult GetPairingCode()
    {
        // Get active code or generate new one
        var activeCode = _context.PairingCodes
            .Where(pc => pc.expires_at > DateTime.UtcNow)
            .OrderByDescending(pc => pc.created_at)
            .FirstOrDefault();

        if (activeCode == null)
        {
            GenerateNewCode();
            activeCode = _context.PairingCodes
                .OrderByDescending(pc => pc.created_at)
                .First();
        }

        return Ok(new { 
            code = activeCode.code, 
            expiresAt = activeCode.expires_at,
            validForMinutes = CodeValidityDuration.TotalMinutes
        });
    }

    [HttpPost("/Pairing/verify")]
    public async Task<IActionResult> VerifyPairingCode([FromBody] VerifyCodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(new { message = "Code is required" });
        }

        // Find active pairing code
        var pairingCode = await _context.PairingCodes
            .Where(pc => pc.code == request.Code.Trim() && pc.expires_at > DateTime.UtcNow)
            .FirstOrDefaultAsync();

        if (pairingCode == null)
        {
            _logger.LogWarning("Invalid or expired pairing code attempted: {Code}", request.Code);
            return BadRequest(new { message = "Invalid or expired pairing code. Please request a new code." });
        }

        // Code is valid, generate permanent token
        var token = Guid.NewGuid().ToString("N");
        
        // Remove all existing tokens (only one token should exist)
        var existingTokens = await _context.AgentTokens.ToListAsync();
        _context.AgentTokens.RemoveRange(existingTokens);

        // Save new token
        var agentToken = new AgentToken
        {
            token = token,
            created_at = DateTime.UtcNow
        };
        _context.AgentTokens.Add(agentToken);

        // Remove the used pairing code
        _context.PairingCodes.Remove(pairingCode);

        // Remove all expired codes
        var expiredCodes = await _context.PairingCodes
            .Where(pc => pc.expires_at <= DateTime.UtcNow)
            .ToListAsync();
        _context.PairingCodes.RemoveRange(expiredCodes);

        await _context.SaveChangesAsync();

        _logger.LogInformation("Pairing successful. Agent token generated and saved to database.");
        Console.WriteLine("\n========================================");
        Console.WriteLine("  PAIRING SUCCESSFUL!");
        Console.WriteLine("  Agent token generated and saved");
        Console.WriteLine("========================================\n");

        return Ok(new { 
            message = "Pairing successful",
            token = token
        });
    }

    [HttpGet("/Pairing/status")]
    public async Task<IActionResult> GetPairingStatus()
    {
        var hasToken = await _context.AgentTokens.AnyAsync();
        var activeCode = await _context.PairingCodes
            .Where(pc => pc.expires_at > DateTime.UtcNow)
            .OrderByDescending(pc => pc.created_at)
            .FirstOrDefaultAsync();

        return Ok(new { 
            isPaired = hasToken,
            hasActiveCode = activeCode != null,
            codeExpiresAt = activeCode?.expires_at
        });
    }

    [HttpPost("/Pairing/authenticate")]
    public async Task<IActionResult> Authenticate([FromHeader(Name = "X-Agent-Token")] string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized(new { message = "Agent token is required" });
        }

        var agentToken = await _context.AgentTokens
            .Where(t => t.token == token)
            .FirstOrDefaultAsync();

        if (agentToken == null)
        {
            _logger.LogWarning("Invalid agent token attempted");
            return Unauthorized(new { message = "Invalid agent token" });
        }

        return Ok(new { message = "Authentication successful" });
    }
}

public record VerifyCodeRequest(string Code);

