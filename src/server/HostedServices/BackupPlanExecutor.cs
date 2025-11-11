using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using server.Data;
using server.Models;

namespace server.HostedServices
{
    public class BackupPlanExecutor
    {
        private readonly ILogger<BackupPlanExecutor> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IWebHostEnvironment _environment;

        public BackupPlanExecutor(
            ILogger<BackupPlanExecutor> logger, 
            IServiceScopeFactory serviceScopeFactory,
            IWebHostEnvironment environment)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _environment = environment;
        }

        public async Task ExecuteBackupPlanAsync(BackupPlan backupPlan, Agent agent)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DBContext>();

                _logger.LogInformation("Executing backup plan {BackupPlanId} for agent {AgentHostname}", backupPlan.id, agent.hostname);

                // Reload agent from database to ensure we have the latest token
                var agentFromDb = await dbContext.Agents.FindAsync(agent.id);
                if (agentFromDb == null)
                {
                    _logger.LogError("Agent {AgentId} not found in database", agent.id);
                    throw new InvalidOperationException($"Agent {agent.id} not found");
                }

                // Check if agent has a token
                if (string.IsNullOrEmpty(agentFromDb.token))
                {
                    _logger.LogError("Agent {AgentHostname} does not have a token. Cannot execute backup plan {BackupPlanId}", 
                        agentFromDb.hostname, backupPlan.id);
                    throw new InvalidOperationException($"Agent {agentFromDb.hostname} is not authenticated. Please pair the agent first.");
                }

                // Call the /Look endpoint to get file system items
                var fileSystemItems = await CallLookEndpointAsync(agentFromDb, backupPlan.source);
                
                _logger.LogInformation("Retrieved {Count} file system items from agent {AgentHostname} for path {Source}", 
                    fileSystemItems.Count, agentFromDb.hostname, backupPlan.source);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing backup plan {BackupPlanId}", backupPlan.id);
                throw;
            }
        }

        private async Task<List<FileSystemItem>> CallLookEndpointAsync(Agent agent, string sourcePath)
        {
            // Determine base URL - try HTTPS first, then HTTP
            string baseUrl;
            var hostname = agent.hostname;
            
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
                    var testUrl = $"{protocol}{hostname}/Look?dir={Uri.EscapeDataString(sourcePath)}";
                    var result = await TryCallLookEndpointAsync(testUrl, agent.token!);
                    if (result.Success && result.Items != null)
                    {
                        return result.Items;
                    }
                }
                throw new HttpRequestException($"Failed to connect to agent at {agent.hostname}");
            }

            var lookUrl = $"{baseUrl}/Look?dir={Uri.EscapeDataString(sourcePath)}";
            var response = await TryCallLookEndpointAsync(lookUrl, agent.token!);
            
            if (!response.Success)
            {
                throw new HttpRequestException($"Failed to call /Look endpoint: {response.ErrorMessage}");
            }

            return response.Items ?? new List<FileSystemItem>();
        }

        private async Task<(bool Success, List<FileSystemItem>? Items, string ErrorMessage)> TryCallLookEndpointAsync(string url, string agentToken)
        {
            // Configure HttpClient to accept self-signed certificates in development
            var httpClientHandler = new HttpClientHandler();
            
            if (_environment.IsDevelopment())
            {
                httpClientHandler.ServerCertificateCustomValidationCallback = 
                    (HttpRequestMessage message, X509Certificate2? certificate, X509Chain? chain, System.Net.Security.SslPolicyErrors sslPolicyErrors) =>
                    {
                        return true;
                    };
            }

            using var httpClient = new HttpClient(httpClientHandler);
            httpClient.Timeout = TimeSpan.FromMinutes(5); // Longer timeout for file system operations
            
            // Add the authentication token header
            httpClient.DefaultRequestHeaders.Add("X-Agent-Token", agentToken);

            try
            {
                _logger.LogInformation("Calling /Look endpoint at {Url}", url);

                var response = await httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var items = await response.Content.ReadFromJsonAsync<List<FileSystemItem>>();
                    _logger.LogInformation("Successfully retrieved {Count} items from /Look endpoint", items?.Count ?? 0);
                    return (true, items ?? new List<FileSystemItem>(), string.Empty);
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
                    _logger.LogWarning("Failed to call /Look endpoint at {Url}: {StatusCode}, {Error}", 
                        url, response.StatusCode, errorContent);
                    return (false, null, $"HTTP {response.StatusCode}: {errorContent}");
                }
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Timeout calling /Look endpoint at {Url}", url);
                return (false, null, "Request timeout");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling /Look endpoint at {Url}", url);
                return (false, null, ex.Message);
            }
        }
    }
}