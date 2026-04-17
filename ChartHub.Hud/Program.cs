using Avalonia;

namespace ChartHub.Hud;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        int serverPort = ParseServerPort(args);

        BuildAvaloniaApp(serverPort)
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp(int serverPort = 5000) =>
        AppBuilder.Configure(() => new App(serverPort))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static int ParseServerPort(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--server-port", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out int port)
                && port > 0)
            {
                return port;
            }
        }

        return 5000;
    }
}
