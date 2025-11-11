namespace server.Models;

/// <summary>
/// Represents a file or directory item from the agent's file system
/// </summary>
public class FileSystemItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "file" or "directory"
    public long? Size { get; set; } // null for directories
    public DateTime LastModified { get; set; }
    public string? Permissions { get; set; }
    public string? Md5 { get; set; } // MD5 hash (only for files)
}

