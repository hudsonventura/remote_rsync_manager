using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using AgentCommon.Data;

namespace AgentCommon.Attributes;

/// <summary>
/// Attribute that requires the X-Agent-Token header to be present and valid
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAgentTokenAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Get the token from the header
        if (!context.HttpContext.Request.Headers.TryGetValue("X-Agent-Token", out var tokenHeader) || 
            string.IsNullOrWhiteSpace(tokenHeader))
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Agent token is required. Please provide X-Agent-Token header." });
            return;
        }

        var token = tokenHeader.ToString();

        // Get the database context from DI
        var dbContext = context.HttpContext.RequestServices.GetRequiredService<AgentDbContext>();

        // Verify the token exists in the database
        var agentToken = await dbContext.AgentTokens
            .Where(t => t.token == token)
            .FirstOrDefaultAsync();

        if (agentToken == null)
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<RequireAgentTokenAttribute>>();
            logger.LogWarning("Invalid agent token attempted from {RemoteIpAddress}", 
                context.HttpContext.Connection.RemoteIpAddress);
            
            context.Result = new UnauthorizedObjectResult(new { message = "Invalid agent token" });
            return;
        }

        // Token is valid, continue with the request
        // Optionally, we could store the token in HttpContext.Items for use in the controller
        context.HttpContext.Items["AgentToken"] = agentToken;
    }
}

