using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using server.Services;

namespace server.Hubs;

[Authorize]
public class TerminalHub : Hub
{
    private readonly TerminalService _terminalService;
    private readonly ILogger<TerminalHub> _logger;

    public TerminalHub(TerminalService terminalService, ILogger<TerminalHub> logger)
    {
        _terminalService = terminalService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Terminal client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Terminal client disconnected: {ConnectionId}", Context.ConnectionId);
        await _terminalService.DisconnectAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task StartTerminal()
    {
        try
        {
            // Capture the client proxy before starting the terminal
            // ISingleClientProxy implements IClientProxy, so we can cast it
            IClientProxy clientProxy = Clients.Caller;
            var connectionId = Context.ConnectionId;
            
            await _terminalService.StartTerminalAsync(connectionId, clientProxy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting terminal for connection {ConnectionId}", Context.ConnectionId);
            try
            {
                await Clients.Caller.SendAsync("TerminalError", ex.Message);
            }
            catch (ObjectDisposedException)
            {
                // Hub context already disposed, ignore
                _logger.LogWarning("Hub context disposed while sending error message");
            }
        }
    }

    public async Task SendInput(string input)
    {
        try
        {
            await _terminalService.SendInputAsync(Context.ConnectionId, input);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending input for connection {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("TerminalError", ex.Message);
        }
    }

    public async Task ResizeTerminal(int cols, int rows)
    {
        try
        {
            await _terminalService.ResizeTerminalAsync(Context.ConnectionId, cols, rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resizing terminal for connection {ConnectionId}", Context.ConnectionId);
        }
    }
}

