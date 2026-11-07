using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace server.Services;

public class ExtensionProcessTracker
{
    private readonly ConcurrentDictionary<Guid, RunningExecutionState> _running = new();

    public void Register(Guid executionId, Guid extensionId, Process process, CancellationTokenSource cts, StringBuilder outputBuilder)
    {
        _running[executionId] = new RunningExecutionState
        {
            ExecutionId = executionId,
            ExtensionId = extensionId,
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
                state.Cts.Cancel();
                if (!state.Process.HasExited)
                {
                    state.Process.Kill(entireProcessTree: true);
                }
                return true;
            }
            catch
            {
                return false;
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
        public required Process Process { get; set; }
        public required CancellationTokenSource Cts { get; set; }
        public required StringBuilder OutputBuilder { get; set; }
        public DateTime StartTime { get; set; }
    }
}
