using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using AgentCommon.Data;
using AgentCommon.Models;

namespace agentWindows.ViewModels;

public class PairingViewModel : INotifyPropertyChanged
{
    private readonly AgentDbContext _context;
    private readonly DispatcherTimer _timer;
    private string _pairingCode = "Loading...";
    private string _expiresAt = "";
    private string _timeRemaining = "";
    private bool _isPaired = false;
    private string _status = "Initializing...";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PairingCode
    {
        get => _pairingCode;
        set
        {
            if (_pairingCode != value)
            {
                _pairingCode = value;
                OnPropertyChanged();
            }
        }
    }

    public string ExpiresAt
    {
        get => _expiresAt;
        set
        {
            if (_expiresAt != value)
            {
                _expiresAt = value;
                OnPropertyChanged();
            }
        }
    }

    public string TimeRemaining
    {
        get => _timeRemaining;
        set
        {
            if (_timeRemaining != value)
            {
                _timeRemaining = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsPaired
    {
        get => _isPaired;
        set
        {
            if (_isPaired != value)
            {
                _isPaired = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotPaired));
            }
        }
    }

    public bool IsNotPaired => !_isPaired;

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }

    public PairingViewModel(AgentDbContext context)
    {
        _context = context;
        
        // Create a timer to update the UI every second
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += async (s, e) => await UpdatePairingInfo();
        _timer.Start();

        // Initial load
        Task.Run(async () => await UpdatePairingInfo());
    }

    private async Task UpdatePairingInfo()
    {
        try
        {
            // Check if agent is already paired
            var hasToken = await _context.AgentTokens.AnyAsync();
            
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsPaired = hasToken;
            });

            if (hasToken)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Status = "✓ Agent is paired and ready";
                    PairingCode = "N/A";
                    ExpiresAt = "Already paired";
                    TimeRemaining = "";
                });
                return;
            }

            // Get active pairing code
            var activeCode = await _context.PairingCodes
                .Where(pc => pc.expires_at > DateTime.UtcNow)
                .OrderByDescending(pc => pc.created_at)
                .FirstOrDefaultAsync();

            if (activeCode != null)
            {
                var timeLeft = activeCode.expires_at - DateTime.UtcNow;
                
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    PairingCode = FormatPairingCode(activeCode.code);
                    ExpiresAt = activeCode.expires_at.ToString("yyyy-MM-dd HH:mm:ss UTC");
                    
                    if (timeLeft.TotalSeconds > 0)
                    {
                        var minutes = (int)timeLeft.TotalMinutes;
                        var seconds = (int)timeLeft.TotalSeconds % 60;
                        TimeRemaining = $"{minutes:D2}:{seconds:D2}";
                        Status = "⏳ Waiting for pairing...";
                    }
                    else
                    {
                        TimeRemaining = "EXPIRED";
                        Status = "⚠ Code expired. Refresh to generate a new one.";
                    }
                });
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    PairingCode = "No active code";
                    ExpiresAt = "";
                    TimeRemaining = "";
                    Status = "⚠ No active pairing code. Refresh to generate one.";
                });
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = $"Error: {ex.Message}";
            });
        }
    }

    private string FormatPairingCode(string code)
    {
        // Format as XXX-XXX for better readability
        if (code.Length == 6)
        {
            return $"{code.Substring(0, 3)}-{code.Substring(3, 3)}";
        }
        return code;
    }

    public async Task RefreshCode()
    {
        try
        {
            // Remove expired codes
            var expiredCodes = await _context.PairingCodes
                .Where(pc => pc.expires_at <= DateTime.UtcNow)
                .ToListAsync();
            _context.PairingCodes.RemoveRange(expiredCodes);

            // Generate new code
            var random = new Random();
            var code = random.Next(100000, 999999).ToString();
            var expiresAt = DateTime.UtcNow.Add(TimeSpan.FromMinutes(10));

            var pairingCode = new PairingCode
            {
                code = code,
                created_at = DateTime.UtcNow,
                expires_at = expiresAt
            };

            _context.PairingCodes.Add(pairingCode);
            await _context.SaveChangesAsync();

            await UpdatePairingInfo();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = $"Error refreshing code: {ex.Message}";
            });
        }
    }

    public async Task UnpairAgent()
    {
        try
        {
            // Remove all agent tokens
            var tokens = await _context.AgentTokens.ToListAsync();
            _context.AgentTokens.RemoveRange(tokens);
            
            // Remove all pairing codes
            var codes = await _context.PairingCodes.ToListAsync();
            _context.PairingCodes.RemoveRange(codes);
            
            await _context.SaveChangesAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = "Agent unpaired. Generating new pairing code...";
            });

            // Generate a new pairing code
            await RefreshCode();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Status = $"Error unpairing agent: {ex.Message}";
            });
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

