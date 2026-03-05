using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.X11;
using RhythmVerseClient;

namespace RhythmVerseClient;

class Program
{
    // This code is platform specific for Linux (X11)
    [STAThread]
    static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseSkia()
            .WithInterFont()
            .LogToTrace();
}
