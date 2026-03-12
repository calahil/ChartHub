using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AsyncImageLoader;
using Microsoft.Extensions.DependencyInjection;
using RhythmVerseClient.Utilities;
using RhythmVerseClient.Views;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient
{
    public partial class App : Avalonia.Application
    {
        public static IServiceProvider? ServiceProvider { get; set; }

        public override void Initialize()
        {
            // Register custom image loader with 404 fallback support
            var fallbackLoader = new FallbackAsyncImageLoader();
            ImageLoader.AsyncImageLoader = fallbackLoader;

            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            ServiceProvider ??= AppBootstrapper.CreateServiceProvider();
            var mainViewModel = ServiceProvider.GetRequiredService<MainViewModel>();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                var mainWindow = new MainWindow();
                mainWindow.DataContext = mainViewModel;
                desktopLifetime.MainWindow = mainWindow;
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewLifetime)
            {
                singleViewLifetime.MainView = new MainView
                {
                    DataContext = mainViewModel
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
