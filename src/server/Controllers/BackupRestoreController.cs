using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using server.Data;
using server.Models;

namespace server.Controllers;

[ApiController]
[Authorize]
public class BackupRestoreController : ControllerBase
{
    private readonly DBContext _context;
    private readonly ILogger<BackupRestoreController> _logger;

    public BackupRestoreController(DBContext context, ILogger<BackupRestoreController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }
        return await _context.Users.FindAsync(userId);
    }

    private async Task<bool> IsCurrentUserAdminAsync()
    {
        var currentUser = await GetCurrentUserAsync();
        return currentUser?.isAdmin ?? false;
    }

    [HttpGet("/api/backup-restore/export")]
    public async Task<IActionResult> ExportConfigurations()
    {
        try
        {
            if (!await IsCurrentUserAdminAsync())
            {
                return Forbid();
            }

            var users = await _context.Users.AsNoTracking().ToListAsync();
            var agents = await _context.Agents.AsNoTracking().ToListAsync();
            var backupPlans = await _context.BackupPlans
                .Include(bp => bp.agent)
                .AsNoTracking()
                .ToListAsync();

            var exportDto = new ConfigExportDto
            {
                Metadata = new ConfigExportMetadata
                {
                    Version = "1.0",
                    ExportedAt = DateTime.UtcNow,
                    ServerVersion = "1.0"
                },
                Users = users.Select(u => new ExportedUserDto
                {
                    Id = u.id,
                    Username = u.username,
                    Email = u.email,
                    PasswordHash = u.passwordHash,
                    IsAdmin = u.isAdmin,
                    CreatedAt = u.createdAt,
                    UpdatedAt = u.updatedAt,
                    IsActive = u.isActive,
                    Timezone = u.timezone,
                    Theme = u.theme,
                    SecurityStamp = u.securityStamp
                }).ToList(),
                Agents = agents.Select(a => new ExportedAgentDto
                {
                    Id = a.id,
                    Name = a.name,
                    Hostname = a.hostname,
                    Token = a.token,
                    RsyncUser = a.rsyncUser,
                    RsyncPort = a.rsyncPort,
                    RsyncSshKey = a.rsyncSshKey
                }).ToList(),
                BackupPlans = backupPlans.Select(bp => new ExportedBackupPlanDto
                {
                    Id = bp.id,
                    Name = bp.name,
                    Description = bp.description,
                    Schedule = bp.schedule,
                    Source = bp.source,
                    Destination = bp.destination,
                    Active = bp.active,
                    RsyncHost = bp.rsyncHost,
                    RsyncUser = bp.rsyncUser,
                    RsyncPort = bp.rsyncPort,
                    RsyncSshKey = bp.rsyncSshKey,
                    AgentId = bp.agent?.id,
                    AgentName = bp.agent?.name
                }).ToList()
            };

            var fileName = $"RemoteRSyncManager-{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}.json";
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var jsonString = JsonSerializer.Serialize(exportDto, jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(jsonString);

            _logger.LogInformation("Exported configuration successfully: {UserCount} users, {AgentCount} agents, {PlanCount} backup plans",
                exportDto.Users.Count, exportDto.Agents.Count, exportDto.BackupPlans.Count);

            return File(bytes, "application/json", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting configurations");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error exporting configurations" });
        }
    }

    [HttpPost("/api/backup-restore/preview")]
    public async Task<IActionResult> PreviewImport([FromBody] ConfigExportDto payload)
    {
        try
        {
            if (!await IsCurrentUserAdminAsync())
            {
                return Forbid();
            }

            if (payload == null)
            {
                return BadRequest(new { message = "Invalid export file format" });
            }

            var existingUsers = await _context.Users.AsNoTracking().ToListAsync();
            var existingAgents = await _context.Agents.AsNoTracking().ToListAsync();
            var existingPlans = await _context.BackupPlans
                .Include(bp => bp.agent)
                .AsNoTracking()
                .ToListAsync();

            var response = new ConflictPreviewResponse
            {
                ExistingAgents = existingAgents.Select(a => new SimpleAgentSummaryDto
                {
                    Id = a.id,
                    Name = a.name,
                    Hostname = a.hostname
                }).ToList()
            };

            // 1. Analyze Users
            if (payload.Users != null)
            {
                foreach (var impUser in payload.Users)
                {
                    var match = existingUsers.FirstOrDefault(u => 
                        u.id == impUser.Id ||
                        u.username.Equals(impUser.Username, StringComparison.OrdinalIgnoreCase) ||
                        u.email.Equals(impUser.Email, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        string reason = match.id == impUser.Id 
                            ? "User ID matches existing record"
                            : match.username.Equals(impUser.Username, StringComparison.OrdinalIgnoreCase)
                                ? $"Username '{impUser.Username}' is already taken"
                                : $"Email '{impUser.Email}' is already registered";

                        response.ConflictedUsers.Add(new UserConflictDto
                        {
                            Imported = impUser,
                            Existing = new ExportedUserDto
                            {
                                Id = match.id,
                                Username = match.username,
                                Email = match.email,
                                PasswordHash = match.passwordHash,
                                IsAdmin = match.isAdmin,
                                CreatedAt = match.createdAt,
                                UpdatedAt = match.updatedAt,
                                IsActive = match.isActive,
                                Timezone = match.timezone,
                                Theme = match.theme,
                                SecurityStamp = match.securityStamp
                            },
                            Reason = reason
                        });
                    }
                    else
                    {
                        response.NewUsers.Add(impUser);
                    }
                }
            }

            // 2. Analyze Agents
            if (payload.Agents != null)
            {
                foreach (var impAgent in payload.Agents)
                {
                    var match = existingAgents.FirstOrDefault(a => 
                        a.id == impAgent.Id ||
                        a.hostname.Equals(impAgent.Hostname, StringComparison.OrdinalIgnoreCase) ||
                        a.name.Equals(impAgent.Name, StringComparison.OrdinalIgnoreCase));

                    var associatedPlans = payload.BackupPlans?
                        .Where(p => p.AgentId == impAgent.Id || (p.AgentName != null && p.AgentName.Equals(impAgent.Name, StringComparison.OrdinalIgnoreCase)))
                        .ToList() ?? new List<ExportedBackupPlanDto>();

                    if (match != null)
                    {
                        string reason = match.id == impAgent.Id
                            ? "Agent ID matches existing record"
                            : match.hostname.Equals(impAgent.Hostname, StringComparison.OrdinalIgnoreCase)
                                ? $"Hostname '{impAgent.Hostname}' matches existing agent '{match.name}'"
                                : $"Agent name '{impAgent.Name}' already exists";

                        response.ConflictedAgents.Add(new AgentConflictDto
                        {
                            Imported = impAgent,
                            Existing = new ExportedAgentDto
                            {
                                Id = match.id,
                                Name = match.name,
                                Hostname = match.hostname,
                                Token = match.token,
                                RsyncUser = match.rsyncUser,
                                RsyncPort = match.rsyncPort,
                                RsyncSshKey = match.rsyncSshKey
                            },
                            Reason = reason,
                            AssociatedPlans = associatedPlans
                        });
                    }
                    else
                    {
                        response.NewAgents.Add(impAgent);
                    }
                }
            }

            // 3. Analyze Backup Plans
            if (payload.BackupPlans != null)
            {
                foreach (var impPlan in payload.BackupPlans)
                {
                    var match = existingPlans.FirstOrDefault(p =>
                        p.id == impPlan.Id ||
                        p.name.Equals(impPlan.Name, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        string reason = match.id == impPlan.Id
                            ? "Plan ID matches existing record"
                            : $"Backup plan name '{impPlan.Name}' already exists";

                        response.ConflictedBackupPlans.Add(new BackupPlanConflictDto
                        {
                            Imported = impPlan,
                            Existing = new ExportedBackupPlanDto
                            {
                                Id = match.id,
                                Name = match.name,
                                Description = match.description,
                                Schedule = match.schedule,
                                Source = match.source,
                                Destination = match.destination,
                                Active = match.active,
                                RsyncHost = match.rsyncHost,
                                RsyncUser = match.rsyncUser,
                                RsyncPort = match.rsyncPort,
                                RsyncSshKey = match.rsyncSshKey,
                                AgentId = match.agent?.id,
                                AgentName = match.agent?.name
                            },
                            Reason = reason
                        });
                    }
                    else
                    {
                        response.NewBackupPlans.Add(impPlan);
                    }
                }
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error previewing configuration import");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error previewing configuration import" });
        }
    }

    [HttpPost("/api/backup-restore/import")]
    public async Task<IActionResult> ExecuteImport([FromBody] ImportResolutionRequest request)
    {
        try
        {
            if (!await IsCurrentUserAdminAsync())
            {
                return Forbid();
            }

            if (request?.Payload == null)
            {
                return BadRequest(new { message = "Invalid import request" });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            var result = new ImportResultDto();
            var agentIdMap = new Dictionary<Guid, Guid>();

            // 1. Process Users
            if (request.Payload.Users != null)
            {
                foreach (var impUser in request.Payload.Users)
                {
                    var existingUser = await _context.Users.FirstOrDefaultAsync(u =>
                        u.id == impUser.Id ||
                        u.username.ToLower() == impUser.Username.ToLower() ||
                        u.email.ToLower() == impUser.Email.ToLower());

                    if (existingUser != null)
                    {
                        var action = request.UserResolutions.GetValueOrDefault(impUser.Id.ToString(), "keep_existing");
                        if (action == "overwrite")
                        {
                            existingUser.passwordHash = impUser.PasswordHash;
                            existingUser.isAdmin = impUser.IsAdmin;
                            existingUser.timezone = impUser.Timezone;
                            existingUser.theme = impUser.Theme;
                            existingUser.isActive = impUser.IsActive;
                            existingUser.securityStamp = string.IsNullOrEmpty(impUser.SecurityStamp) ? Guid.NewGuid().ToString() : impUser.SecurityStamp;
                            existingUser.updatedAt = DateTime.UtcNow;

                            result.UsersUpdated++;
                        }
                        else
                        {
                            result.UsersSkipped++;
                        }
                    }
                    else
                    {
                        var newUser = new User
                        {
                            id = impUser.Id == Guid.Empty ? Guid.NewGuid() : impUser.Id,
                            username = impUser.Username.Trim(),
                            email = impUser.Email.Trim(),
                            passwordHash = impUser.PasswordHash,
                            isAdmin = impUser.IsAdmin,
                            createdAt = impUser.CreatedAt == default ? DateTime.UtcNow : impUser.CreatedAt,
                            updatedAt = impUser.UpdatedAt,
                            isActive = impUser.IsActive,
                            timezone = impUser.Timezone,
                            theme = impUser.Theme,
                            securityStamp = string.IsNullOrEmpty(impUser.SecurityStamp) ? Guid.NewGuid().ToString() : impUser.SecurityStamp
                        };

                        _context.Users.Add(newUser);
                        result.UsersImported++;
                    }
                }
                await _context.SaveChangesAsync();
            }

            // 2. Process Agents
            if (request.Payload.Agents != null)
            {
                foreach (var impAgent in request.Payload.Agents)
                {
                    var existingAgent = await _context.Agents.FirstOrDefaultAsync(a =>
                        a.id == impAgent.Id ||
                        a.hostname.ToLower() == impAgent.Hostname.ToLower() ||
                        a.name.ToLower() == impAgent.Name.ToLower());

                    if (existingAgent != null)
                    {
                        var action = request.AgentResolutions.GetValueOrDefault(impAgent.Id.ToString(), "keep_existing");
                        if (action == "overwrite")
                        {
                            existingAgent.name = impAgent.Name;
                            existingAgent.hostname = impAgent.Hostname;
                            existingAgent.token = impAgent.Token;
                            existingAgent.rsyncUser = impAgent.RsyncUser;
                            existingAgent.rsyncPort = impAgent.RsyncPort;
                            existingAgent.rsyncSshKey = impAgent.RsyncSshKey;

                            agentIdMap[impAgent.Id] = existingAgent.id;
                            result.AgentsUpdated++;
                        }
                        else if (action == "create_new")
                        {
                            var newAgentId = Guid.NewGuid();
                            var newAgent = new Agent
                            {
                                id = newAgentId,
                                name = $"{impAgent.Name} (Imported)",
                                hostname = impAgent.Hostname,
                                token = impAgent.Token,
                                rsyncUser = impAgent.RsyncUser,
                                rsyncPort = impAgent.RsyncPort,
                                rsyncSshKey = impAgent.RsyncSshKey
                            };

                            _context.Agents.Add(newAgent);
                            agentIdMap[impAgent.Id] = newAgentId;
                            result.AgentsImported++;
                        }
                        else if (action == "skip")
                        {
                            result.AgentsSkipped++;
                        }
                        else // keep_existing
                        {
                            agentIdMap[impAgent.Id] = existingAgent.id;
                            result.AgentsSkipped++;
                        }
                    }
                    else
                    {
                        var newAgentId = impAgent.Id == Guid.Empty ? Guid.NewGuid() : impAgent.Id;
                        var newAgent = new Agent
                        {
                            id = newAgentId,
                            name = impAgent.Name,
                            hostname = impAgent.Hostname,
                            token = impAgent.Token,
                            rsyncUser = impAgent.RsyncUser,
                            rsyncPort = impAgent.RsyncPort,
                            rsyncSshKey = impAgent.RsyncSshKey
                        };

                        _context.Agents.Add(newAgent);
                        agentIdMap[impAgent.Id] = newAgentId;
                        result.AgentsImported++;
                    }
                }
                await _context.SaveChangesAsync();
            }

            // Apply explicit plan-to-agent reassignments (AgentPlanMappings)
            if (request.AgentPlanMappings != null)
            {
                foreach (var (importedAgentIdStr, targetAgentIdStr) in request.AgentPlanMappings)
                {
                    if (Guid.TryParse(importedAgentIdStr, out var importedAgentId) &&
                        Guid.TryParse(targetAgentIdStr, out var targetAgentId))
                    {
                        agentIdMap[importedAgentId] = targetAgentId;
                    }
                }
            }

            // 3. Process Backup Plans
            if (request.Payload.BackupPlans != null)
            {
                foreach (var impPlan in request.Payload.BackupPlans)
                {
                    // Resolve target agent
                    Agent? targetAgent = null;
                    if (impPlan.AgentId.HasValue && agentIdMap.TryGetValue(impPlan.AgentId.Value, out var resolvedAgentId))
                    {
                        targetAgent = await _context.Agents.FindAsync(resolvedAgentId);
                    }
                    else if (impPlan.AgentId.HasValue)
                    {
                        targetAgent = await _context.Agents.FindAsync(impPlan.AgentId.Value);
                    }
                    else if (!string.IsNullOrEmpty(impPlan.AgentName))
                    {
                        targetAgent = await _context.Agents.FirstOrDefaultAsync(a => a.name.ToLower() == impPlan.AgentName.ToLower());
                    }

                    var existingPlan = await _context.BackupPlans
                        .Include(bp => bp.agent)
                        .FirstOrDefaultAsync(p =>
                            p.id == impPlan.Id ||
                            p.name.ToLower() == impPlan.Name.ToLower());

                    if (existingPlan != null)
                    {
                        var action = request.PlanResolutions.GetValueOrDefault(impPlan.Id.ToString(), "keep_existing");
                        if (action == "overwrite")
                        {
                            existingPlan.name = impPlan.Name;
                            existingPlan.description = impPlan.Description;
                            existingPlan.schedule = impPlan.Schedule;
                            existingPlan.source = impPlan.Source;
                            existingPlan.destination = impPlan.Destination;
                            existingPlan.active = impPlan.Active;
                            existingPlan.rsyncHost = targetAgent?.hostname ?? impPlan.RsyncHost;
                            existingPlan.rsyncUser = targetAgent?.rsyncUser ?? impPlan.RsyncUser;
                            existingPlan.rsyncPort = targetAgent?.rsyncPort ?? impPlan.RsyncPort;
                            existingPlan.rsyncSshKey = targetAgent?.rsyncSshKey ?? impPlan.RsyncSshKey;
                            existingPlan.agent = targetAgent ?? existingPlan.agent;

                            result.PlansUpdated++;
                        }
                        else if (action == "create_new")
                        {
                            var newPlan = new BackupPlan
                            {
                                id = Guid.NewGuid(),
                                name = $"{impPlan.Name} (Imported)",
                                description = impPlan.Description,
                                schedule = impPlan.Schedule,
                                source = impPlan.Source,
                                destination = impPlan.Destination,
                                active = impPlan.Active,
                                rsyncHost = targetAgent?.hostname ?? impPlan.RsyncHost,
                                rsyncUser = targetAgent?.rsyncUser ?? impPlan.RsyncUser,
                                rsyncPort = targetAgent?.rsyncPort ?? impPlan.RsyncPort,
                                rsyncSshKey = targetAgent?.rsyncSshKey ?? impPlan.RsyncSshKey,
                                agent = targetAgent
                            };

                            _context.BackupPlans.Add(newPlan);
                            result.PlansImported++;
                        }
                        else
                        {
                            result.PlansSkipped++;
                        }
                    }
                    else
                    {
                        var planId = impPlan.Id == Guid.Empty ? Guid.NewGuid() : impPlan.Id;
                        // Avoid primary key collision just in case
                        if (await _context.BackupPlans.AnyAsync(p => p.id == planId))
                        {
                            planId = Guid.NewGuid();
                        }

                        var newPlan = new BackupPlan
                        {
                            id = planId,
                            name = impPlan.Name,
                            description = impPlan.Description,
                            schedule = impPlan.Schedule,
                            source = impPlan.Source,
                            destination = impPlan.Destination,
                            active = impPlan.Active,
                            rsyncHost = targetAgent?.hostname ?? impPlan.RsyncHost,
                            rsyncUser = targetAgent?.rsyncUser ?? impPlan.RsyncUser,
                            rsyncPort = targetAgent?.rsyncPort ?? impPlan.RsyncPort,
                            rsyncSshKey = targetAgent?.rsyncSshKey ?? impPlan.RsyncSshKey,
                            agent = targetAgent
                        };

                        _context.BackupPlans.Add(newPlan);
                        result.PlansImported++;
                    }
                }
                await _context.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            result.Success = true;
            result.Message = "Configurations imported and merged successfully.";

            _logger.LogInformation("Import completed: Users ({Imported}/{Updated}/{Skipped}), Agents ({AImp}/{AUpd}/{ASkip}), Plans ({PImp}/{PUpd}/{PSkip})",
                result.UsersImported, result.UsersUpdated, result.UsersSkipped,
                result.AgentsImported, result.AgentsUpdated, result.AgentsSkipped,
                result.PlansImported, result.PlansUpdated, result.PlansSkipped);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing configuration import");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Error executing configuration import: " + ex.Message });
        }
    }
}
