namespace server.Models;

public class BackupPlanResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Schedule { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public bool Active { get; set; } = false;
    public Guid? AgentId { get; set; }
    public string? AgentHostname { get; set; }
}

