namespace server.Models;

public record UpdateAgentRequest(
    string Hostname, 
    string? Name = null,
    string? RsyncUser = null,
    int? RsyncPort = null,
    string? RsyncSshKey = null
);

