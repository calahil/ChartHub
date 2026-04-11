using System.Runtime.ExceptionServices;
using System.Text.Json;

using Avalonia;

using ChartHub.Services;
using ChartHub.Utilities;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChartHub;

class Program
{
    // This code is platform specific for Linux (X11)
    [STAThread]
    static void Main(string[] args)
    {
        IServiceProvider serviceProvider = AppBootstrapper.CreateServiceProvider();
        App.ServiceProvider = serviceProvider;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                // Avoid noisy/benign IBus DBus context teardown errors on Linux X11.
                EnableIme = false,
            })
            .UseSkia()
            .WithInterFont()
            .LogToTrace();
    }

}
