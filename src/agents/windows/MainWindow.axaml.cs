using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;
using AgentCommon.Data;
using agentWindows.ViewModels;

namespace agentWindows;

public partial class MainWindow : Window
{
    private PairingViewModel? _viewModel;
    private bool _isExiting = false;

    public MainWindow()
    {
        InitializeComponent();
        InitializeViewModel();
        
        // Handle window closing event
        Closing += OnWindowClosing;
        
        // Ensure window is shown and brought to front
        this.Opened += (s, e) =>
        {
            Show();
            Activate();
            Topmost = true;
            Topmost = false; // Reset to allow other windows on top later
        };
    }

    private async void InitializeViewModel()
    {
        // Get database path from configuration
        var dataDirectory = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "data");
        var connectionString = $"Data Source={System.IO.Path.Combine(dataDirectory, "agent.db")}";

        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite(connectionString)
            .Options;

        var dbContext = new AgentDbContext(options);
        
        _viewModel = new PairingViewModel(dbContext);
        DataContext = _viewModel;
    }

    private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.RefreshCode();
        }
    }

    private async void UnpairButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.UnpairAgent();
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        // If not explicitly exiting, minimize to tray instead of closing
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
        }
    }

    public void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ExitApplication()
    {
        _isExiting = true;
        Close();
        
        // Shutdown the application
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}

