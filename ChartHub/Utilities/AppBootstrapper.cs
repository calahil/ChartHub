using System.Text.Json;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Migration;
using ChartHub.Configuration.Models;
using ChartHub.Configuration.Stores;
using ChartHub.Services;
using ChartHub.ViewModels;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChartHub.Utilities;

internal sealed class UserSecretsAnchor
{
}

public static class AppBootstrapper
{
    public static IServiceProvider CreateServiceProvider()
    {
        return CreateServiceProvider(configDirOverride: null, migrationActionOverride: null);
    }

    internal static IServiceProvider CreateServiceProvider(
        string? configDirOverride,
        Func<IServiceProvider, Task>? migrationActionOverride)
    {
        var services = new ServiceCollection();
        Logger.LogInfo("Bootstrap", "Configuring service collection");
        ConfigureServices(services, configDirOverride);
        ServiceProvider provider = services.BuildServiceProvider();
        Logger.LogInfo("Bootstrap", "Service provider created");

        if (OperatingSystem.IsAndroid())
        {
            _ = Task.Run(async () => await RunPostBuildInitializationAsync(provider, migrationActionOverride));
        }
        else
        {
            RunPostBuildInitializationAsync(provider, migrationActionOverride).GetAwaiter().GetResult();
        }

        return provider;
    }

    private static async Task RunPostBuildInitializationAsync(
        IServiceProvider provider,
        Func<IServiceProvider, Task>? migrationActionOverride)
    {
        try
        {
            Logger.LogInfo("Bootstrap", "Starting settings migration");
            if (migrationActionOverride is not null)
            {
                await migrationActionOverride(provider).ConfigureAwait(false);
            }
            else
            {
                await provider.GetRequiredService<SettingsMigrationService>()
                    .MigrateLegacySecretsAsync()
                    .ConfigureAwait(false);
            }
            Logger.LogInfo("Bootstrap", "Settings migration completed");
        }
        catch (Exception ex)
        {
            Logger.LogError("Bootstrap", "Settings migration failed during bootstrap", ex);
        }
    }

    private static void ConfigureServices(IServiceCollection services, string? configDirOverride)
    {
        string configDir = string.IsNullOrWhiteSpace(configDirOverride)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChartHub")
            : configDirOverride;
        FileTools.CreateDirectoryIfNotExists(configDir);
        Logger.LogInfo("Bootstrap", "Using application config directory", new Dictionary<string, object?>
        {
            ["configDir"] = configDir,
        });

        // Build configuration with user secrets
        IConfigurationBuilder configBuilder = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(configDir, "appsettings.json"), optional: true, reloadOnChange: true)
            .AddUserSecrets<UserSecretsAnchor>(optional: true)
            .AddEnvironmentVariables();

        IConfigurationRoot config = configBuilder.Build();
        services.AddSingleton<IConfiguration>(config);

        string configPath = Path.Combine(configDir, "appsettings.json");
        services.AddSingleton<IAppConfigStore>(_ => new JsonAppConfigStore(configPath));
        services.AddSingleton<ISecretStore>(_ =>
            OperatingSystem.IsAndroid()
                ? new AndroidSecretStore(configDir)
                : new DesktopSecretStore(configDir));
        services.AddSingleton<IConfigValidator, DefaultConfigValidator>();
        services.AddSingleton<ISettingsOrchestrator, SettingsOrchestrator>();
        services.AddSingleton<SettingsMigrationService>();

        string settingsFileName = "appsettings.json";
        string sourceFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, settingsFileName);
        string destinationFilePath = Path.Combine(configDir, settingsFileName);

        if (!File.Exists(destinationFilePath))
        {
            if (File.Exists(sourceFilePath))
            {
                File.Copy(sourceFilePath, destinationFilePath);
            }
            else
            {
                // Android does not always bundle appsettings.json as a plain file path.
                var defaultConfig = new AppConfigRoot();
                string defaultJson = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = null,
                });
                File.WriteAllText(destinationFilePath, defaultJson);
            }
        }

        services.AddSingleton<AppGlobalSettings>();

        if (OperatingSystem.IsAndroid())
        {
            services.AddSingleton<IGoogleAuthProvider, AndroidGoogleAuthProvider>();
        }
        else
        {
            services.AddSingleton<IGoogleAuthProvider, DesktopGoogleAuthProvider>();
        }

#if ANDROID
        services.AddSingleton<IQrCodeScannerService, AndroidQrCodeScannerService>();
#else
        services.AddSingleton<IQrCodeScannerService, NoOpQrCodeScannerService>();
#endif
        services.AddSingleton<EncoreApiService>();
        services.AddSingleton<SharedDownloadQueue>();
        services.AddSingleton(_ => new LibraryCatalogService(Path.Combine(configDir, "library-catalog.db")));
        services.AddSingleton<IChartHubServerApiClient, ChartHubServerApiClient>();
        services.AddSingleton<DownloadViewModel>(serviceProvider =>
            new DownloadViewModel(
                serviceProvider.GetRequiredService<AppGlobalSettings>(),
                serviceProvider.GetRequiredService<IChartHubServerApiClient>(),
                serviceProvider.GetRequiredService<SharedDownloadQueue>(),
                serviceProvider.GetService<CloneHeroViewModel>()));
        services.AddSingleton<CloneHeroViewModel>(serviceProvider =>
            new CloneHeroViewModel(
                serviceProvider.GetRequiredService<AppGlobalSettings>(),
                serviceProvider.GetRequiredService<IChartHubServerApiClient>()));
        services.AddSingleton<DesktopEntryViewModel>(serviceProvider =>
            new DesktopEntryViewModel(
                serviceProvider.GetRequiredService<AppGlobalSettings>(),
                serviceProvider.GetRequiredService<IChartHubServerApiClient>()));
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<RhythmVerseViewModel>(serviceProvider =>
            new RhythmVerseViewModel(
                serviceProvider.GetRequiredService<IConfiguration>(),
                serviceProvider.GetRequiredService<LibraryCatalogService>(),
                serviceProvider.GetRequiredService<SharedDownloadQueue>(),
                serviceProvider.GetRequiredService<ISettingsOrchestrator>(),
                serviceProvider.GetRequiredService<IChartHubServerApiClient>()));
        services.AddSingleton<EncoreViewModel>();
        services.AddSingleton<MainViewModel>(serviceProvider =>
            new MainViewModel(
                serviceProvider.GetRequiredService<RhythmVerseViewModel>(),
                serviceProvider.GetRequiredService<EncoreViewModel>(),
                serviceProvider.GetRequiredService<SharedDownloadQueue>(),
                serviceProvider.GetRequiredService<DownloadViewModel>(),
                serviceProvider.GetRequiredService<CloneHeroViewModel>(),
                serviceProvider.GetRequiredService<DesktopEntryViewModel>(),
                serviceProvider.GetRequiredService<SettingsViewModel>()
            )
        );
        services.AddSingleton<Initializer>();
    }
}
