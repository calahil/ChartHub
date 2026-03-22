using System.Threading;
using System.Threading.Tasks;

using AsyncImageLoader;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using ChartHub.Services;
using ChartHub.Utilities;
using ChartHub.ViewModels;
using ChartHub.Views;

using Microsoft.Extensions.DependencyInjection;

namespace ChartHub;

public partial class App : Avalonia.Application
{
    public static IServiceProvider? ServiceProvider { get; set; }
    private static int _shutdownHandled;

    public override void Initialize()
    {
        // Register custom image loader with 404 fallback support
        var fallbackLoader = new FallbackAsyncImageLoader();
        ImageLoader.AsyncImageLoader = fallbackLoader;

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Logger.Initialize();
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        ServiceProvider ??= AppBootstrapper.CreateServiceProvider();
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        ICloudStorageAccountService cloudAccountService = ServiceProvider.GetRequiredService<ICloudStorageAccountService>();
        IIngestionSyncApiHost ingestionSyncApiHost = ServiceProvider.GetRequiredService<IIngestionSyncApiHost>();
        var shellViewModel = new AppShellViewModel(ServiceProvider, cloudAccountService);
        Logger.LogInfo("App", "Framework initialization completed");

        ObserveBackgroundTask(ingestionSyncApiHost.StartAsync(), "Ingestion sync API startup");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            var mainWindow = new MainWindow();
            mainWindow.DataContext = shellViewModel;
            desktopLifetime.MainWindow = mainWindow;
            desktopLifetime.Exit += OnApplicationExit;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewLifetime)
        {
            singleViewLifetime.MainView = new AppShellView
            {
                DataContext = shellViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        DisposeApplicationResourcesOnce();
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        DisposeApplicationResourcesOnce();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Logger.LogCritical(
                "App",
                "Unhandled application exception",
                exception,
                new Dictionary<string, object?>
                {
                    ["isTerminating"] = e.IsTerminating,
                });
            return;
        }

        Logger.LogCritical(
            "App",
            "Unhandled non-exception application failure",
            new InvalidOperationException("AppDomain unhandled exception object was not an Exception instance."),
            new Dictionary<string, object?>
            {
                ["isTerminating"] = e.IsTerminating,
            });
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logger.LogError("App", "Unobserved task exception", e.Exception);
    }

    private static void ObserveBackgroundTask(Task task, string context)
    {
        _ = task.ContinueWith(t =>
        {
            Exception? ex = t.Exception?.GetBaseException();
            if (ex is not null)
            {
                Logger.LogError("App", $"{context} failed", ex);
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private static void DisposeApplicationResourcesOnce()
    {
        if (Interlocked.Exchange(ref _shutdownHandled, 1) != 0)
        {
            return;
        }

        try
        {
            Logger.LogInfo("App", "Application resource shutdown started");

            if (ImageLoader.AsyncImageLoader is IDisposable imageLoaderDisposable)
            {
                imageLoaderDisposable.Dispose();
            }

            if (ServiceProvider is IAsyncDisposable asyncDisposable)
            {
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            else if (ServiceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            Logger.LogInfo("App", "Application resource shutdown completed");
        }
        catch (Exception ex)
        {
            Logger.LogError("App", "Shutdown cleanup failed", ex);
        }
        finally
        {
            Logger.Shutdown();
        }
    }
}
