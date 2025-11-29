namespace server.Models;

public class ExecutionResult
{
    public List<ExecutionItems> Items { get; set; } = new();
    public int TotalItems { get; set; }
    public int ItemsToCopy { get; set; }
    public int ItemsToDelete { get; set; }
    
    // Statistics from rsync --stats output
    public int TotalFiles { get; set; }
    public int RegularFiles { get; set; }
    public int Directories { get; set; }
    public int CreatedFiles { get; set; }
    public int DeletedFiles { get; set; }
    public int TransferredFiles { get; set; }
    public long TotalFileSize { get; set; }
    public long TotalTransferredSize { get; set; }
    public long LiteralData { get; set; }
    public long MatchedData { get; set; }
    public long FileListSize { get; set; }
    public double FileListGenerationTime { get; set; }
    public double FileListTransferTime { get; set; }
    public long TotalBytesSent { get; set; }
    public long TotalBytesReceived { get; set; }
    public double TransferSpeedBytesPerSecond { get; set; }
    public double Speedup { get; set; }
    public double DurationSeconds { get; set; }
}

public class ExecutionItems
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long? Size { get; set; }
    public string Action { get; set; } = string.Empty; // "Copy" or "Delete"
    public string Reason { get; set; } = string.Empty;
}

