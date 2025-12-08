using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using System.Collections;

namespace server.Services;

public class TerminalService
{
    private readonly Dictionary<string, Process> _processes = new();
    private readonly Dictionary<string, IClientProxy> _clientProxies = new();
    private readonly ILogger<TerminalService> _logger;

    public TerminalService(ILogger<TerminalService> logger)
    {
        _logger = logger;
    }

    public async Task StartTerminalAsync(string connectionId, IClientProxy? clientProxy)
    {
        if (clientProxy == null)
        {
            _logger.LogError("Client proxy is null for connection {ConnectionId}", connectionId);
            throw new ArgumentNullException(nameof(clientProxy));
        }

        if (_processes.ContainsKey(connectionId))
        {
            _logger.LogWarning("Terminal already exists for connection {ConnectionId}", connectionId);
            return;
        }

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = "-i -l", // Interactive login shell to load profile
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Inherit environment variables from the current process
            // This ensures PATH and other important variables are available
            foreach (var envVar in Environment.GetEnvironmentVariables().Keys)
            {
                var key = envVar.ToString();
                if (key != null && !processStartInfo.Environment.ContainsKey(key))
                {
                    var value = Environment.GetEnvironmentVariable(key);
                    if (value != null)
                    {
                        processStartInfo.Environment[key] = value;
                    }
                }
            }

            // Set environment variables for proper terminal behavior
            processStartInfo.Environment["TERM"] = "xterm-256color";
            processStartInfo.Environment["COLUMNS"] = "80";
            processStartInfo.Environment["LINES"] = "24";
            
            // Ensure PATH is set (fallback if not inherited)
            if (!processStartInfo.Environment.ContainsKey("PATH") || string.IsNullOrEmpty(processStartInfo.Environment["PATH"]))
            {
                processStartInfo.Environment["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";
            }
            
            // Clear any existing environment variables that might interfere
            processStartInfo.Environment.Remove("PS1");

            var process = new Process
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                // OutputDataReceived can send null for empty lines
                if (e.Data != null)
                {
                    outputBuilder.Append(e.Data);
                    outputBuilder.Append("\n");
                    // Send with \r\n for proper terminal line breaks
                    // Fire and forget - don't await in event handler
                    _ = SendOutputAsync(connectionId, e.Data + "\r\n");
                }
                else
                {
                    // Empty line - send just the line break
                    _ = SendOutputAsync(connectionId, "\r\n");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                // ErrorDataReceived can send null for empty lines
                if (e.Data != null)
                {
                    errorBuilder.Append(e.Data);
                    // Send with \r\n for proper terminal line breaks
                    // Fire and forget - don't await in event handler
                    _ = SendOutputAsync(connectionId, e.Data + "\r\n");
                }
                else
                {
                    // Empty line - send just the line break
                    _ = SendOutputAsync(connectionId, "\r\n");
                }
            };

            process.Exited += (sender, e) =>
            {
                _logger.LogInformation("Terminal process exited for connection {ConnectionId}", connectionId);
                Cleanup(connectionId);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _processes[connectionId] = process;
            _clientProxies[connectionId] = clientProxy;

            _logger.LogInformation("Terminal started for connection {ConnectionId}", connectionId);

            // Configure bash to echo input properly
            // Send stty command to enable echo (redirect output to avoid "Display all possibilities" prompt)
            await Task.Delay(100);
            await process.StandardInput.WriteLineAsync("stty echo 2>/dev/null");
            await process.StandardInput.FlushAsync();
            
            // Send initial prompt
            await Task.Delay(50);
            await SendOutputAsync(connectionId, "$ ");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start terminal for connection {ConnectionId}", connectionId);
            throw;
        }
    }

    public async Task SendInputAsync(string connectionId, string input)
    {
        if (!_processes.TryGetValue(connectionId, out var process))
        {
            _logger.LogWarning("No terminal found for connection {ConnectionId}", connectionId);
            return;
        }

        try
        {
            if (process.HasExited)
            {
                _logger.LogWarning("Terminal process has exited for connection {ConnectionId}", connectionId);
                Cleanup(connectionId);
                return;
            }

            // Write input directly to stdin
            // For password prompts (like sudo), characters won't be echoed back
            // but they will still be sent to the process
            // Note: Some programs like sudo read from /dev/tty directly, which won't work
            // with redirected stdin. For those cases, you may need to use a PTY library.
            await process.StandardInput.WriteAsync(input);
            await process.StandardInput.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending input to terminal for connection {ConnectionId}", connectionId);
            throw;
        }
    }

    public async Task ResizeTerminalAsync(string connectionId, int cols, int rows)
    {
        if (!_processes.TryGetValue(connectionId, out var process))
        {
            return;
        }

        try
        {
            // On Linux, we can use stty to resize the terminal
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync($"stty cols {cols} rows {rows}");
                await process.StandardInput.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resize terminal for connection {ConnectionId}", connectionId);
        }
    }

    public async Task DisconnectAsync(string connectionId)
    {
        Cleanup(connectionId);
        await Task.CompletedTask;
    }

    private async Task SendOutputAsync(string connectionId, string output)
    {
        if (_clientProxies.TryGetValue(connectionId, out var clientProxy))
        {
            try
            {
                await clientProxy.SendAsync("TerminalOutput", output);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error sending output to client {ConnectionId}, cleaning up", connectionId);
                // If we can't send, the connection is likely dead, clean up
                Cleanup(connectionId);
            }
        }
    }

    private void Cleanup(string connectionId)
    {
        if (_processes.TryGetValue(connectionId, out var process))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
                process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cleaning up terminal for connection {ConnectionId}", connectionId);
            }
            finally
            {
                _processes.Remove(connectionId);
                _clientProxies.Remove(connectionId);
            }
        }
    }
}

