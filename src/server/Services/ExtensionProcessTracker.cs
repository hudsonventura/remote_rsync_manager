using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace server.Services;

public class ExtensionProcessTracker
{
    private readonly ConcurrentDictionary<Guid, RunningExecutionState> _running = new();

    public void Register(Guid executionId, Guid extensionId, Process process, int processId, CancellationTokenSource cts, StringBuilder outputBuilder)
    {
        _running[executionId] = new RunningExecutionState
        {
            ExecutionId = executionId,
            ExtensionId = extensionId,
            ProcessId = processId,
            Process = process,
            Cts = cts,
            OutputBuilder = outputBuilder,
            StartTime = DateTime.UtcNow
        };
    }

    public void Unregister(Guid executionId)
    {
        _running.TryRemove(executionId, out _);
    }

    public string? GetLiveOutput(Guid executionId)
    {
        if (_running.TryGetValue(executionId, out var state))
        {
            lock (state.OutputBuilder)
            {
                return state.OutputBuilder.ToString();
            }
        }
        return null;
    }

    public bool IsRunning(Guid extensionId)
    {
        return _running.Values.Any(s => s.ExtensionId == extensionId);
    }

    public Guid? GetRunningExecutionId(Guid extensionId)
    {
        return _running.Values.FirstOrDefault(s => s.ExtensionId == extensionId)?.ExecutionId;
    }

    public bool StopExecution(Guid executionId)
    {
        if (_running.TryGetValue(executionId, out var state))
        {
            try
            {
                // Kill process tree first using OS kill and process.Kill BEFORE cancelling CTS
                ProcessHelper.KillProcessTree(state.Process, state.ProcessId);
                state.Cts.Cancel();
                return true;
            }
            catch
            {
                // Fallback: kill by PID directly
                ProcessHelper.KillProcessTree(null, state.ProcessId);
                state.Cts.Cancel();
                return true;
            }
        }
        return false;
    }

    public bool StopExtension(Guid extensionId)
    {
        var state = _running.Values.FirstOrDefault(s => s.ExtensionId == extensionId);
        if (state != null)
        {
            return StopExecution(state.ExecutionId);
        }
        return false;
    }

    private class RunningExecutionState
    {
        public Guid ExecutionId { get; set; }
        public Guid ExtensionId { get; set; }
        public int ProcessId { get; set; }
        public required Process Process { get; set; }
        public required CancellationTokenSource Cts { get; set; }
        public required StringBuilder OutputBuilder { get; set; }
        public DateTime StartTime { get; set; }
    }
}
