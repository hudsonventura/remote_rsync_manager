using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace agentWindows;

public partial class App : Application
{
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void TrayIcon_Clicked(object? sender, EventArgs e)
    {
        _mainWindow?.ShowWindow();
    }

    private void ShowWindow_Click(object? sender, EventArgs e)
    {
        _mainWindow?.ShowWindow();
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        _mainWindow?.ExitApplication();
    }
}

