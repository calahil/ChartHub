using System.Text.Json;
using System.Text.Json.Nodes;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Migration;
using ChartHub.Configuration.Models;
using ChartHub.Configuration.Stores;
using ChartHub.Services;
using ChartHub.Services.Transfers;
using ChartHub.ViewModels;

using Google.Apis.Drive.v3;

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
            int removed = await provider.GetRequiredService<LibraryCatalogService>()
                .RemoveMissingLocalFilesAsync()
                .ConfigureAwait(false);
            Logger.LogInfo("Bootstrap", "Catalog reconciliation completed", new Dictionary<string, object?>
            {
                ["removedEntries"] = removed,
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Bootstrap", "Catalog reconciliation failed", new Dictionary<string, object?>
            {
                ["error"] = ex.Message,
            });
        }

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

        SyncGoogleDriveConfig(sourceFilePath, destinationFilePath);

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

        services.AddSingleton<IGoogleDriveClient, GoogleDriveClient>();
        services.AddSingleton<ICloudStorageAccountService, GoogleCloudStorageAccountService>();
        services.AddSingleton<DownloadService>();
        services.AddSingleton<EncoreApiService>();
        services.AddSingleton<SharedDownloadQueue>();
        services.AddSingleton(_ => new LibraryCatalogService(Path.Combine(configDir, "library-catalog.db")));
        services.AddSingleton(_ => new SongIngestionCatalogService(Path.Combine(configDir, "library-catalog.db")));
        services.AddSingleton<SongIngestionStateMachine>();
        services.AddSingleton<ISongIniMetadataParser, SongIniMetadataParser>();
        services.AddSingleton<ICloneHeroDirectorySchemaService, CloneHeroDirectorySchemaService>();
        services.AddSingleton<ICloneHeroLibraryReconciliationService, CloneHeroLibraryReconciliationService>();
        services.AddSingleton<IOnyxPipelineService, OnyxService>();
        services.AddSingleton<ISongInstallService, SongInstallService>();
        services.AddSingleton<IDesktopPathOpener, DesktopPathOpener>();
        services.AddSingleton<ITransferSourceResolver, TransferSourceResolver>();
        services.AddSingleton<ILocalDestinationWriter, LocalDestinationWriter>();
        services.AddSingleton<IGoogleDriveDestinationWriter, GoogleDriveDestinationWriter>();
        services.AddSingleton<ITransferOrchestrator, TransferOrchestrator>();
        services.AddSingleton<ISyncAdvertisedUrlOptionsProvider, SyncAdvertisedUrlOptionsProvider>();
        services.AddSingleton<ISyncLanDiscoveryService, SyncLanDiscoveryService>();
        services.AddSingleton<IIngestionSyncApiHost, IngestionSyncApiHost>();
        services.AddSingleton<IDesktopSyncApiClient, DesktopSyncApiClient>();
        services.AddSingleton<DownloadViewModel>();
        services.AddSingleton<CloneHeroViewModel>();
        services.AddSingleton<SyncViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<RhythmVerseViewModel>();
        services.AddSingleton<EncoreViewModel>();
        services.AddSingleton<MainViewModel>(serviceProvider =>
            new MainViewModel(
                serviceProvider.GetRequiredService<RhythmVerseViewModel>(),
                serviceProvider.GetRequiredService<EncoreViewModel>(),
                serviceProvider.GetRequiredService<SharedDownloadQueue>(),
                serviceProvider.GetRequiredService<DownloadViewModel>(),
                serviceProvider.GetRequiredService<CloneHeroViewModel>(),
                serviceProvider.GetRequiredService<SyncViewModel>(),
                serviceProvider.GetRequiredService<SettingsViewModel>()
            )
        );
        services.AddSingleton<DriveService>();
        services.AddSingleton<Initializer>();
    }

    private static void SyncGoogleDriveConfig(string sourceFilePath, string destinationFilePath)
    {
        if (!File.Exists(destinationFilePath))
        {
            return;
        }

        JsonObject destinationRoot;
        try
        {
            destinationRoot = JsonNode.Parse(File.ReadAllText(destinationFilePath)) as JsonObject ?? [];
        }
        catch
        {
            destinationRoot = [];
        }

        JsonObject sourceRoot = [];
        if (File.Exists(sourceFilePath))
        {
            try
            {
                sourceRoot = JsonNode.Parse(File.ReadAllText(sourceFilePath)) as JsonObject ?? [];
            }
            catch
            {
                sourceRoot = [];
            }
        }

        JsonObject destinationGoogle = destinationRoot["GoogleDrive"] as JsonObject ?? [];
        var sourceGoogle = sourceRoot["GoogleDrive"] as JsonObject;

        if (sourceGoogle is not null)
        {
            foreach (KeyValuePair<string, JsonNode?> key in sourceGoogle)
            {
                if (!destinationGoogle.ContainsKey(key.Key) && key.Value is not null)
                {
                    destinationGoogle[key.Key] = key.Value.DeepClone();
                }
            }
        }

        destinationRoot["GoogleDrive"] = destinationGoogle;
        File.WriteAllText(destinationFilePath, destinationRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
