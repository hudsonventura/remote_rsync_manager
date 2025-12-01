namespace server.Models;

public record CreateAgentRequest(
    string Hostname, 
    string? Name = null,
    string? RsyncUser = null,
    int? RsyncPort = null,
    string? RsyncSshKey = null
);

