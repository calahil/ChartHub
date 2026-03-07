using System.Text.Json;
using Avalonia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RhythmVerseClient.Services;
using RhythmVerseClient.Utilities;
using SettingsManager;
using RhythmVerseClient.ViewModels;
using Google.Apis.Drive.v3;

namespace RhythmVerseClient;

class Program
{
    // This code is platform specific for Linux (X11)
    [STAThread]
    static void Main(string[] args)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);

        var serviceProvider = services.BuildServiceProvider();
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

    private static void ConfigureServices(IServiceCollection services)
    {
        var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RhythmVerseClient");
        Toolbox.CreateDirectoryIfNotExists(configDir);

        // Build configuration with user secrets
        var configBuilder = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(configDir, "appsettings.json"), optional: true)
            .AddUserSecrets<Program>(optional: true);

        var config = configBuilder.Build();
        services.AddSingleton<IConfiguration>(config);

        var settingsFileName = "appsettings.json";
            var sourceFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, settingsFileName);
            var destinationFilePath = Path.Combine(configDir, settingsFileName);

            if (!File.Exists(destinationFilePath))
            {
                File.Copy(sourceFilePath, destinationFilePath);
            }

            string json = File.ReadAllText(destinationFilePath);

            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonCerealOptions.Instance)
                    ?? throw new JsonException("Failed to deserialize settings.");


            services.AddSingleton(settings);

            services.AddSingleton<ISettingsManager<AppSettings>>(serviceProvider =>
            {
                var settingsFilePath = Path.Combine(configDir, settingsFileName);
                var appSettings = serviceProvider.GetRequiredService<AppSettings>();
                return new SettingsManager<AppSettings>(settingsFilePath, appSettings);
            });

        services.AddSingleton<AppGlobalSettings>();
        services.AddSingleton<IGoogleDriveClient, GoogleDriveClient>();
        services.AddSingleton<DownloadViewModel>();
        services.AddSingleton<CloneHeroViewModel>();
        services.AddSingleton<InstallSongViewModel>();
        services.AddSingleton<RhythmVerseModel>();
        services.AddSingleton<MainViewModel>(serviceProvider =>
            new MainViewModel(
                serviceProvider.GetRequiredService<RhythmVerseModel>(),
                serviceProvider.GetRequiredService<DownloadViewModel>(),
                serviceProvider.GetRequiredService<CloneHeroViewModel>(),
                serviceProvider.GetRequiredService<InstallSongViewModel>()
            )
        );
        services.AddSingleton<DriveService>();
        services.AddSingleton<Initializer>();
    }
}
