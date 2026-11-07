using Microsoft.AspNetCore.Http;

namespace server.Models;

public class UpdateExtensionRequest
{
    public string name { get; set; } = string.Empty;
    public string? description { get; set; }
    public string command { get; set; } = "dotnet App.dll";
    public string schedule { get; set; } = "0 * * * *";
    public bool active { get; set; } = true;
    public string? environmentVariables { get; set; }
    public IFormFile? zipFile { get; set; }
}

