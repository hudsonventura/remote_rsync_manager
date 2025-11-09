namespace server.Models;

public record CreateBackupPlanRequest(
    string Name,
    string Description,
    string Schedule,
    string Source,
    string Destination,
    Guid AgentId
);

