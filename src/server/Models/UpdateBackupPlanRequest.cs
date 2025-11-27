namespace server.Models;

public record UpdateBackupPlanRequest(
    string Name,
    string Description,
    string Schedule,
    string Source,
    string Destination,
    string? RsyncHost,
    string? RsyncUser,
    int? RsyncPort,
    string? RsyncSshKey,
    bool Active = true
);

