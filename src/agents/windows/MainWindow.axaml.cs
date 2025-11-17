using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;
using AgentCommon.Data;
using agentWindows.ViewModels;

namespace agentWindows;

public partial class MainWindow : Window
{
    private PairingViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        InitializeViewModel();
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
}

