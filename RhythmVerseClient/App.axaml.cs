using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AsyncImageLoader;
using Microsoft.Extensions.DependencyInjection;
using RhythmVerseClient.Services;
using RhythmVerseClient.Views;
using RhythmVerseClient.ViewModels;
using RhythmVerseClient.Utilities;

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
