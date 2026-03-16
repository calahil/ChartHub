using System.Text.Json;
using System.Runtime.ExceptionServices;
using Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ChartHub.Services;
using ChartHub.Utilities;

namespace ChartHub;

class Program
{
    // This code is platform specific for Linux (X11)
    [STAThread]
    static void Main(string[] args)
    {
        var serviceProvider = AppBootstrapper.CreateServiceProvider();
        App.ServiceProvider = serviceProvider;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseSkia()
            .WithInterFont()
            .LogToTrace();
    }

}
