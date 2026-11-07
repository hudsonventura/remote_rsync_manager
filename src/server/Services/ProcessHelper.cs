using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace server.Services;

public static class ProcessHelper
{
    /// <summary>
    /// Forcefully terminates a process and all of its child/descendant processes.
    /// Uses managed Process.Kill along with native OS-level kill signals (kill -9) on Linux/macOS.
    /// </summary>
    public static void KillProcessTree(Process? process, int pid, ILogger? logger = null)
    {
        if (pid <= 0 && process != null)
        {
            try { pid = process.Id; } catch { }
        }

        // 1. Try managed process.Kill(true) first if process object is available
        try
        {
            if (process != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Managed Process.Kill failed or process already exited for PID {Pid}", pid);
        }

        // 2. On Linux or macOS, use OS-level kill to guarantee termination of the entire tree
        if (pid > 0 && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
        {
            try
            {
                KillUnixProcessTree(pid, logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "OS level kill failed for PID {Pid}", pid);
            }
        }
    }

    private static void KillUnixProcessTree(int rootPid, ILogger? logger)
    {
        var pidsToKill = new List<int>();
        try
        {
            CollectChildPids(rootPid, pidsToKill);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Error collecting child PIDs for {RootPid}", rootPid);
        }

        pidsToKill.Add(rootPid);

        // Terminate children first, then parent
        pidsToKill.Reverse();

        foreach (var p in pidsToKill)
        {
            try
            {
                using var killProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "kill",
                    ArgumentList = { "-9", p.ToString() },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                killProcess?.WaitForExit(1000);
                logger?.LogInformation("Sent SIGKILL (-9) to PID {Pid}", p);
            }
            catch
            {
                // Ignore if process already terminated
            }
        }
    }

    private static void CollectChildPids(int pid, List<int> collected)
    {
        // 1. Check /proc/{pid}/task/{pid}/children (Linux procfs)
        try
        {
            var childrenFile = $"/proc/{pid}/task/{pid}/children";
            if (File.Exists(childrenFile))
            {
                var content = File.ReadAllText(childrenFile).Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    var childPids = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var c in childPids)
                    {
                        if (int.TryParse(c, out int childPid) && childPid > 0 && !collected.Contains(childPid))
                        {
                            CollectChildPids(childPid, collected);
                            collected.Add(childPid);
                        }
                    }
                }
            }
        }
        catch { }

        // 2. Scan /proc/*/stat for processes whose PPID is this pid
        try
        {
            if (Directory.Exists("/proc"))
            {
                foreach (var dir in Directory.GetDirectories("/proc"))
                {
                    var dirName = Path.GetFileName(dir);
                    if (int.TryParse(dirName, out int candidatePid) && candidatePid > 0 && candidatePid != pid)
                    {
                        try
                        {
                            var statFile = Path.Combine(dir, "stat");
                            if (File.Exists(statFile))
                            {
                                var content = File.ReadAllText(statFile);
                                // The comm field is enclosed in parentheses, e.g. (dotnet)
                                var lastParen = content.LastIndexOf(')');
                                if (lastParen >= 0 && lastParen + 2 < content.Length)
                                {
                                    var fields = content.Substring(lastParen + 2).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                    // Field 0 is state, Field 1 is ppid
                                    if (fields.Length > 1 && int.TryParse(fields[1], out int ppid) && ppid == pid)
                                    {
                                        if (!collected.Contains(candidatePid))
                                        {
                                            CollectChildPids(candidatePid, collected);
                                            collected.Add(candidatePid);
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
    }
}
