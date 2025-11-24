namespace AgentCommon.Models;

/// <summary>
/// Represents a file or directory item
/// </summary>
public class FileSystemItem
{
    /// <summary>
    /// Filenam with extension
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full path and filename with extension
    /// </summary>
    public string PathName { get; set; } = string.Empty;

    /// <summary>
    /// Full path without filename
    /// </summary>
    public string Path { get; set; } = string.Empty;


    /// <summary>
    /// Type of item: "file" or "directory"
    /// </summary>
    public string Type { get; set; } = string.Empty; // "file" or "directory"

    /// <summary>
    /// Size of item in bytes
    /// </summary>
    public long? Size { get; set; } // null for directories

    /// <summary>
    /// Last modified date and time
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// Permissions of item
    /// </summary>
    public string? Permissions { get; set; }
    
    /// <summary>
    /// MD5 hash of item
    /// </summary>
    public string? Md5 { get; set; } // MD5 hash (only for files)
}
