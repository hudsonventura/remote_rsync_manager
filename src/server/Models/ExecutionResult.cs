namespace server.Models;

public class ExecutionResult
{
    public List<ExecutionItems> Items { get; set; } = new();
    public int TotalItems { get; set; }
    public int ItemsToCopy { get; set; }
    public int ItemsToDelete { get; set; }
}

public class ExecutionItems
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long? Size { get; set; }
    public string Action { get; set; } = string.Empty; // "Copy" or "Delete"
    public string Reason { get; set; } = string.Empty;
}

