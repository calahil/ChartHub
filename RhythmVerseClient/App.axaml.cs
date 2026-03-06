using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RhythmVerseClient.Views;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient
{
    public partial class App : Avalonia.Application
    {
        public static IServiceProvider? ServiceProvider { get; set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                var mainWindow = new MainView();
                mainWindow.DataContext = ServiceProvider?.GetRequiredService<MainViewModel>();
                desktopLifetime.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
