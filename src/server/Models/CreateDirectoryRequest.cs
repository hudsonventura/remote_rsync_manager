namespace server.Models;

public class CreateDirectoryRequest
{
    public string? ParentPath { get; set; }
    public string? DirectoryName { get; set; }
    public string? Path { get; set; }
}
