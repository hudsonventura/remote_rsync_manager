using System;
using System.Collections.Generic;

namespace server.Models;

public class ConfigExportMetadata
{
    public string Version { get; set; } = "1.0";
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public string ServerVersion { get; set; } = "1.0";
}

public class ExportedUserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Timezone { get; set; }
    public string? Theme { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
}

public class ExportedAgentDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string? Token { get; set; }
    public string? RsyncUser { get; set; }
    public int RsyncPort { get; set; } = 22;
    public string? RsyncSshKey { get; set; }
}

public class ExportedBackupPlanDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Schedule { get; set; } = "0 0 * * *";
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public bool Active { get; set; }
    public string? RsyncHost { get; set; }
    public string? RsyncUser { get; set; }
    public int RsyncPort { get; set; } = 22;
    public string? RsyncSshKey { get; set; }
    public Guid? AgentId { get; set; }
    public string? AgentName { get; set; }
}

public class ConfigExportDto
{
    public ConfigExportMetadata Metadata { get; set; } = new();
    public List<ExportedUserDto> Users { get; set; } = new();
    public List<ExportedAgentDto> Agents { get; set; } = new();
    public List<ExportedBackupPlanDto> BackupPlans { get; set; } = new();
}

public class UserConflictDto
{
    public ExportedUserDto Imported { get; set; } = null!;
    public ExportedUserDto Existing { get; set; } = null!;
    public string Reason { get; set; } = string.Empty;
}

public class AgentConflictDto
{
    public ExportedAgentDto Imported { get; set; } = null!;
    public ExportedAgentDto Existing { get; set; } = null!;
    public string Reason { get; set; } = string.Empty;
    public List<ExportedBackupPlanDto> AssociatedPlans { get; set; } = new();
}

public class BackupPlanConflictDto
{
    public ExportedBackupPlanDto Imported { get; set; } = null!;
    public ExportedBackupPlanDto Existing { get; set; } = null!;
    public string Reason { get; set; } = string.Empty;
}

public class SimpleAgentSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
}

public class ConflictPreviewResponse
{
    public List<ExportedUserDto> NewUsers { get; set; } = new();
    public List<UserConflictDto> ConflictedUsers { get; set; } = new();

    public List<ExportedAgentDto> NewAgents { get; set; } = new();
    public List<AgentConflictDto> ConflictedAgents { get; set; } = new();

    public List<ExportedBackupPlanDto> NewBackupPlans { get; set; } = new();
    public List<BackupPlanConflictDto> ConflictedBackupPlans { get; set; } = new();

    public List<SimpleAgentSummaryDto> ExistingAgents { get; set; } = new();
}

public class ImportResolutionRequest
{
    public ConfigExportDto Payload { get; set; } = null!;
    
    // User resolutions: userId -> action ("keep_existing", "overwrite", "skip")
    public Dictionary<string, string> UserResolutions { get; set; } = new();

    // Agent resolutions: agentId -> action ("keep_existing", "overwrite", "create_new", "skip")
    public Dictionary<string, string> AgentResolutions { get; set; } = new();

    // Agent plan re-assignment: importedAgentId -> targetAgentId (Guid string)
    public Dictionary<string, string> AgentPlanMappings { get; set; } = new();

    // Plan resolutions: planId -> action ("keep_existing", "overwrite", "create_new", "skip")
    public Dictionary<string, string> PlanResolutions { get; set; } = new();
}

public class ImportResultDto
{
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;
    public int UsersImported { get; set; }
    public int UsersUpdated { get; set; }
    public int UsersSkipped { get; set; }

    public int AgentsImported { get; set; }
    public int AgentsUpdated { get; set; }
    public int AgentsSkipped { get; set; }

    public int PlansImported { get; set; }
    public int PlansUpdated { get; set; }
    public int PlansSkipped { get; set; }
}
