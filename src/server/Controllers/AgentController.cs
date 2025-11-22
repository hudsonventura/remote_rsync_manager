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

        if (string.IsNullOrWhiteSpace(request.PairingCode))
        {
            return BadRequest(new { message = "Pairing code is required" });
        }

        var hostname = request.Hostname.Trim();
        var pairingCode = request.PairingCode.Trim();

        // Ping the agent to verify it's reachable
        var validationResult = await PingAgent(hostname);

        if (!validationResult.Success)
        {
            return StatusCode(503, new { 
                message = validationResult.ErrorMessage
            });
        }

        // Verify pairing code and get agent token (required)
        var pairingResult = await VerifyPairingCode(hostname, pairingCode);
        if (!pairingResult.Success)
        {
            return StatusCode(400, new { 
                message = pairingResult.ErrorMessage
            });
        }

        var agentToken = pairingResult.Token;
        if (string.IsNullOrEmpty(agentToken))
        {
            return StatusCode(500, new { 
                message = "Failed to generate agent token after pairing verification"
            });
        }

        // Agent is reachable and paired, proceed with creation
        var agent = new Agent
        {
            id = Guid.NewGuid(),
            name = string.IsNullOrWhiteSpace(request.Name) ? "New Agent" : request.Name.Trim(),
            hostname = hostname,
            token = agentToken // Token is required
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
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ValidateAgent(Guid id)
    {
        var agent = await _context.Agents.FindAsync(id);

        if (agent == null)
        {
            return NotFound(new { message = "Agent not found" });
        }

        var hostname = agent.hostname;
        var agentToken = agent.token;

        // First, ping the agent to verify it's reachable
        var pingResult = await PingAgent(hostname);

        if (!pingResult.Success)
        {
            return StatusCode(503, new { 
                message = pingResult.ErrorMessage,
                hostname = hostname
            });
        }

        // If agent has a token, try to call the authenticated endpoint
        if (string.IsNullOrEmpty(agentToken))
        {
            return Ok(new { 
                message = "Agent is reachable but not authenticated. Please pair the agent with a pairing code.", 
                hostname = hostname,
                pingUrl = pingResult.PingUrl,
                response = pingResult.Response,
                authenticated = false
            });
        }

        // Try to call the authenticated endpoint
        var authResult = await CallAuthenticatedEndpoint(hostname, agentToken);

        if (authResult.Success)
        {
            return Ok(new { 
                message = "Agent is reachable and authenticated successfully", 
                hostname = hostname,
                pingUrl = pingResult.PingUrl,
                authenticated = true,
                authResponse = authResult.Response
            });
        }
        else
        {
            return StatusCode(401, new { 
                message = $"Agent is reachable but authentication failed: {authResult.ErrorMessage}",
                hostname = hostname,
                pingUrl = pingResult.PingUrl,
                authenticated = false
            });
        }
    }

    [HttpPost("{id}/reconnect")]
    [ProducesResponseType(typeof(Agent), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ReconnectAgent(Guid id, [FromBody] ReconnectAgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PairingCode))
        {
            return BadRequest(new { message = "Pairing code is required" });
        }

        var agent = await _context.Agents.FindAsync(id);
        if (agent == null)
        {
            return NotFound(new { message = "Agent not found" });
        }

        var hostname = agent.hostname;
        var pairingCode = request.PairingCode.Trim();

        // Ping the agent to verify it's reachable
        var validationResult = await PingAgent(hostname);
        if (!validationResult.Success)
        {
            return StatusCode(503, new { 
                message = validationResult.ErrorMessage
            });
        }

        // Verify pairing code and get agent token
        var pairingResult = await VerifyPairingCode(hostname, pairingCode);
        if (!pairingResult.Success)
        {
            return StatusCode(400, new { 
                message = pairingResult.ErrorMessage
            });
        }

        var agentToken = pairingResult.Token;
        if (string.IsNullOrEmpty(agentToken))
        {
            return StatusCode(500, new { 
                message = "Failed to generate agent token after pairing verification"
            });
        }

        // Update agent token
        agent.token = agentToken;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Agent {AgentId} reconnected successfully with new token", agent.id);

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

            // Update hostname and name
            agent.hostname = request.Hostname.Trim();
            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                agent.name = request.Name.Trim();
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

    [HttpGet("{id}/browse")]
    [ProducesResponseType(typeof(List<FileSystemItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> BrowseAgentFileSystem(Guid id, [FromQuery] string? dir)
    {
        try
        {
            var agent = await _context.Agents.FindAsync(id);

            if (agent == null)
            {
                return NotFound(new { message = "Agent not found" });
            }

            if (string.IsNullOrEmpty(agent.token))
            {
                return Unauthorized(new { message = "Agent is not authenticated. Please pair the agent first." });
            }

            // Determine base URL
            var hostname = agent.hostname;
            string baseUrl;
            
            if (hostname.StartsWith("http://"))
            {
                baseUrl = hostname;
            }
            else if (hostname.StartsWith("https://"))
            {
                baseUrl = hostname;
            }
            else
            {
                // Try HTTPS first, then HTTP as fallback
                string[] protocolsToTry = new[] { "https://", "http://" };
                foreach (var protocol in protocolsToTry)
                {
                    var testUrl = $"{protocol}{hostname}/Browse?dir={Uri.EscapeDataString(dir ?? "/")}";
                    var result = await TryCallBrowseEndpoint(testUrl, agent.token);
                    if (result.Success && result.Items != null)
                    {
                        return Ok(result.Items);
                    }
                }
                return StatusCode(503, new { message = "Failed to connect to agent" });
            }

            var browseUrl = $"{baseUrl}/Browse?dir={Uri.EscapeDataString(dir ?? "/")}";
            var response = await TryCallBrowseEndpoint(browseUrl, agent.token);
            
            if (!response.Success)
            {
                return StatusCode(503, new { message = response.ErrorMessage ?? "Failed to browse agent file system" });
            }

            return Ok(response.Items ?? new List<FileSystemItem>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error browsing agent {AgentId} file system", id);
            return StatusCode(500, new { message = "An error occurred while browsing the file system" });
        }
    }

    private async Task<(bool Success, List<FileSystemItem>? Items, string? ErrorMessage)> TryCallBrowseEndpoint(string url, string agentToken)
    {
        var httpClientHandler = new HttpClientHandler();
        httpClientHandler.ServerCertificateCustomValidationCallback = 
            (HttpRequestMessage message, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
            {
                // Accept all certificates (ignore certificate validation)
                return true;
            };

        using var httpClient = new HttpClient(httpClientHandler);
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        
        httpClient.DefaultRequestHeaders.Add("X-Agent-Token", agentToken);

        try
        {
            _logger.LogInformation("Calling browse endpoint at {Url}", url);

            var response = await httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var items = await response.Content.ReadFromJsonAsync<List<FileSystemItem>>();
                _logger.LogInformation("Browse endpoint call successful, retrieved {Count} items", items?.Count ?? 0);
                return (true, items ?? new List<FileSystemItem>(), null);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Authentication failed at {Url}: {StatusCode}, {Error}", 
                    url, response.StatusCode, errorContent);
                return (false, null, "Authentication failed: Invalid or expired token");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Browse endpoint call failed: {StatusCode}, {Error}", 
                    response.StatusCode, errorContent);
                return (false, null, $"Request failed with status {response.StatusCode}");
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to browse endpoint at {Url}", url);
            return (false, null, $"Cannot reach agent: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("Timeout while calling browse endpoint at {Url}", url);
            return (false, null, "Connection timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling browse endpoint at {Url}", url);
            return (false, null, $"Error: {ex.Message}");
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

    private async Task<(bool Success, string? PingUrl, string? Response, string ErrorMessage)> PingAgent(string hostname)
    {
        var pingSuccessful = false;
        var lastError = "";
        string? successfulPingUrl = null;
        string? responseContent = null;

        // Determine ping URL - try HTTPS first, then HTTP if specified or if HTTPS fails
        string[] protocolsToTry;
        if (hostname.StartsWith("http://"))
        {
            protocolsToTry = new[] { "http://" };
            hostname = hostname.Substring(7);
        }
        else if (hostname.StartsWith("https://"))
        {
            protocolsToTry = new[] { "https://" };
            hostname = hostname.Substring(8);
        }
        else
        {
            // Try HTTPS first, then HTTP as fallback
            protocolsToTry = new[] { "https://", "http://" };
        }

        // Configure HttpClient to accept self-signed certificates (always ignore certificate validation)
        var httpClientHandler = new HttpClientHandler();
        httpClientHandler.ServerCertificateCustomValidationCallback = 
            (HttpRequestMessage message, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
            {
                // Accept all certificates (ignore certificate validation)
                return true;
            };

        using var httpClient = new HttpClient(httpClientHandler);
        httpClient.Timeout = TimeSpan.FromSeconds(5);

        foreach (var protocol in protocolsToTry)
        {
            var pingUrl = $"{protocol}{hostname}/Pong";
            
            try
            {
                _logger.LogInformation("Pinging agent at {PingUrl}", pingUrl);

                var response = await httpClient.GetAsync(pingUrl);

                if (response.IsSuccessStatusCode)
                {
                    responseContent = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("Agent ping successful for {Hostname} at {PingUrl}. Response: {Content}", 
                        hostname, pingUrl, responseContent);
                    pingSuccessful = true;
                    successfulPingUrl = pingUrl;
                    break;
                }
                else
                {
                    _logger.LogWarning("Agent ping failed with status {StatusCode} for {PingUrl}", 
                        response.StatusCode, pingUrl);
                    lastError = $"Status: {response.StatusCode}";
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("Failed to connect to agent at {PingUrl}: {Error}", pingUrl, ex.Message);
                lastError = ex.Message;
                // Continue to next protocol if available
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Timeout while pinging agent at {PingUrl}", pingUrl);
                lastError = "Connection timeout";
                // Continue to next protocol if available
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error pinging agent at {PingUrl}: {Error}", pingUrl, ex.Message);
                lastError = ex.Message;
                // Continue to next protocol if available
            }
        }

        if (!pingSuccessful)
        {
            _logger.LogError("Failed to ping agent at {Hostname} after trying all protocols", hostname);
            return (false, null, null, 
                $"Cannot reach agent at {hostname}. Please verify the hostname is correct, the agent is running, and the /Pong endpoint is accessible. Error: {lastError}");
        }

        return (true, successfulPingUrl, responseContent, string.Empty);
    }

    private async Task<(bool Success, string? Response, string ErrorMessage)> CallAuthenticatedEndpoint(string hostname, string agentToken)
    {
        // Determine base URL - try HTTPS first, then HTTP
        string baseUrl;
        if (hostname.StartsWith("http://"))
        {
            baseUrl = hostname;
            hostname = hostname.Substring(7);
        }
        else if (hostname.StartsWith("https://"))
        {
            baseUrl = hostname;
            hostname = hostname.Substring(8);
        }
        else
        {
            // Try HTTPS first, then HTTP as fallback
            string[] protocolsToTry = new[] { "https://", "http://" };
            foreach (var protocol in protocolsToTry)
            {
                var testUrl = $"{protocol}{hostname}/Backup/status";
                var result = await TryCallAuthenticatedEndpoint(testUrl, agentToken);
                if (result.Success)
                {
                    return result;
                }
            }
            return (false, null, "Failed to connect to authenticated endpoint");
        }

        var authUrl = $"{baseUrl}/Backup/status";
        return await TryCallAuthenticatedEndpoint(authUrl, agentToken);
    }

    private async Task<(bool Success, string? Response, string ErrorMessage)> TryCallAuthenticatedEndpoint(string url, string agentToken)
    {
        // Configure HttpClient to accept self-signed certificates (always ignore certificate validation)
        var httpClientHandler = new HttpClientHandler();
        httpClientHandler.ServerCertificateCustomValidationCallback = 
            (HttpRequestMessage message, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
            {
                // Accept all certificates (ignore certificate validation)
                return true;
            };

        using var httpClient = new HttpClient(httpClientHandler);
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        
        // Add the authentication token header
        httpClient.DefaultRequestHeaders.Add("X-Agent-Token", agentToken);

        try
        {
            _logger.LogInformation("Calling authenticated endpoint at {Url}", url);

            var response = await httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Authenticated endpoint call successful for {Url}. Response: {Content}", 
                    url, responseContent);
                return (true, responseContent, string.Empty);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Authentication failed at {Url}: {StatusCode}, {Error}", 
                    url, response.StatusCode, errorContent);
                return (false, null, "Authentication failed: Invalid or expired token");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Authenticated endpoint call failed: {StatusCode}, {Error}", 
                    response.StatusCode, errorContent);
                return (false, null, $"Request failed with status {response.StatusCode}");
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to authenticated endpoint at {Url}", url);
            return (false, null, $"Cannot reach authenticated endpoint: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("Timeout while calling authenticated endpoint at {Url}", url);
            return (false, null, "Connection timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling authenticated endpoint at {Url}", url);
            return (false, null, $"Error calling authenticated endpoint: {ex.Message}");
        }
    }

    private async Task<(bool Success, string? Token, string ErrorMessage)> VerifyPairingCode(string hostname, string pairingCode)
    {
        // Determine base URL - try HTTPS first, then HTTP
        string baseUrl;
        if (hostname.StartsWith("http://"))
        {
            baseUrl = hostname;
            hostname = hostname.Substring(7);
        }
        else if (hostname.StartsWith("https://"))
        {
            baseUrl = hostname;
            hostname = hostname.Substring(8);
        }
        else
        {
            // Try HTTPS first
            baseUrl = $"https://{hostname}";
        }

        // Configure HttpClient to accept self-signed certificates (always ignore certificate validation)
        var httpClientHandler = new HttpClientHandler();
        httpClientHandler.ServerCertificateCustomValidationCallback = 
            (HttpRequestMessage message, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
            {
                // Accept all certificates (ignore certificate validation)
                return true;
            };

        using var httpClient = new HttpClient(httpClientHandler);
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        try
        {
            var verifyUrl = $"{baseUrl}/Pairing/verify";
            _logger.LogInformation("Verifying pairing code at {VerifyUrl}", verifyUrl);

            var response = await httpClient.PostAsJsonAsync(verifyUrl, new { Code = pairingCode });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<PairingVerifyResponse>();
                if (result != null && !string.IsNullOrEmpty(result.Token))
                {
                    _logger.LogInformation("Pairing code verified successfully for {Hostname}", hostname);
                    return (true, result.Token, string.Empty);
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Pairing code verification failed: {StatusCode}, {Error}", response.StatusCode, errorContent);
                var errorResult = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                return (false, null, errorResult?.Message ?? $"Verification failed with status {response.StatusCode}");
            }
        }
        catch (HttpRequestException ex)
        {
            string msg = ex.Message;
            if(ex.InnerException != null)
                msg += $" - {ex.InnerException.Message}";
            
            _logger.LogError(ex, "Failed to connect to agent for pairing verification at {Hostname}. Error: {Error}", hostname, msg);
            return (false, null, $"Cannot reach agent for pairing verification: {msg}");
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("Timeout while verifying pairing code at {Hostname}", hostname);
            return (false, null, "Timeout while verifying pairing code");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying pairing code at {Hostname}", hostname);
            return (false, null, $"Error verifying pairing code: {ex.Message}");
        }

        return (false, null, "Failed to verify pairing code");
    }

    private class PairingVerifyResponse
    {
        public string? Token { get; set; }
        public string? Message { get; set; }
    }

    private class ErrorResponse
    {
        public string? Message { get; set; }
    }

    public class ReconnectAgentRequest
    {
        public string PairingCode { get; set; } = string.Empty;
    }

    public class CreateAgentRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string PairingCode { get; set; } = string.Empty;
    }

    public class UpdateAgentRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
    }
}

