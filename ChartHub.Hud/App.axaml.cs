using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using ChartHub.Hud.Services;
using ChartHub.Hud.ViewModels;
using ChartHub.Hud.Views;

namespace ChartHub.Hud;

public sealed class App : Application
{
    private readonly int _serverPort;

    public App()
        : this(serverPort: 5000)
    {
    }

    public App(int serverPort)
    {
        _serverPort = serverPort;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ServerStatusService statusService = new(_serverPort);
            ServerVolumeService volumeService = new(_serverPort);
            HudViewModel viewModel = new(statusService, volumeService);
            HudWindow window = new() { DataContext = viewModel };

            desktop.MainWindow = window;

            desktop.Exit += (_, _) =>
            {
                viewModel.Dispose();
                volumeService.Dispose();
                statusService.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
