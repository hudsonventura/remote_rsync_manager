namespace server.Models;

public record UpdateBackupPlanRequest(
    string Name,
    string Description,
    string Schedule,
    string Source,
    string Destination
);

