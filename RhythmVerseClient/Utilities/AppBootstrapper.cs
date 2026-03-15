using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RhythmVerseClient.Services;
using RhythmVerseClient.Services.Transfers;
using RhythmVerseClient.ViewModels;
using SettingsManager;

namespace RhythmVerseClient.Utilities
{
    internal sealed class UserSecretsAnchor
    {
    }

    public static class AppBootstrapper
    {
        public static IServiceProvider CreateServiceProvider()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);

            return services.BuildServiceProvider();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RhythmVerseClient");
            FileTools.CreateDirectoryIfNotExists(configDir);

            // Build configuration with user secrets
            var configBuilder = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(configDir, "appsettings.json"), optional: true)
                .AddUserSecrets<UserSecretsAnchor>(optional: true)
                .AddEnvironmentVariables();

            var config = configBuilder.Build();
            services.AddSingleton<IConfiguration>(config);

            var settingsFileName = "appsettings.json";
            var sourceFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, settingsFileName);
            var destinationFilePath = Path.Combine(configDir, settingsFileName);

            if (!File.Exists(destinationFilePath))
            {
                if (File.Exists(sourceFilePath))
                {
                    File.Copy(sourceFilePath, destinationFilePath);
                }
                else
                {
                    // Android does not always bundle appsettings.json as a plain file path.
                    var defaultSettings = new AppSettings(
                        useMockData: false,
                        tempDirectory: "first_install",
                        downloadDirectory: "first_install",
                        stagingDirectory: "first_install",
                        outputDirectory: "first_install",
                        cloneHeroSongDirectory: "first_install",
                        cloneHeroDataDirectory: "first_install");

                    var defaultJson = JsonSerializer.Serialize(defaultSettings, JsonCerealOptions.Instance);
                    File.WriteAllText(destinationFilePath, defaultJson);
                }
            }

            SyncGoogleDriveConfig(sourceFilePath, destinationFilePath);

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

            if (OperatingSystem.IsAndroid())
                services.AddSingleton<IGoogleAuthProvider, AndroidGoogleAuthProvider>();
            else
                services.AddSingleton<IGoogleAuthProvider, DesktopGoogleAuthProvider>();

            services.AddSingleton<IGoogleDriveClient, GoogleDriveClient>();
            services.AddSingleton<DownloadService>();
            services.AddSingleton<ITransferSourceResolver, TransferSourceResolver>();
            services.AddSingleton<ILocalDestinationWriter, LocalDestinationWriter>();
            services.AddSingleton<IGoogleDriveDestinationWriter, GoogleDriveDestinationWriter>();
            services.AddSingleton<ITransferOrchestrator, TransferOrchestrator>();
            services.AddSingleton<DownloadViewModel>();
            services.AddSingleton<CloneHeroViewModel>();
            services.AddSingleton<InstallSongViewModel>();
            services.AddSingleton<RhythmVerseViewModel>();
            services.AddSingleton<MainViewModel>(serviceProvider =>
                new MainViewModel(
                    serviceProvider.GetRequiredService<RhythmVerseViewModel>(),
                    serviceProvider.GetRequiredService<DownloadViewModel>(),
                    serviceProvider.GetRequiredService<CloneHeroViewModel>(),
                    serviceProvider.GetRequiredService<InstallSongViewModel>()
                )
            );
            services.AddSingleton<DriveService>();
            services.AddSingleton<Initializer>();
        }

        private static void SyncGoogleDriveConfig(string sourceFilePath, string destinationFilePath)
        {
            if (!File.Exists(destinationFilePath))
                return;

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

            var destinationGoogle = destinationRoot["GoogleDrive"] as JsonObject ?? [];
            var sourceGoogle = sourceRoot["GoogleDrive"] as JsonObject;

            if (sourceGoogle is not null)
            {
                foreach (var key in sourceGoogle)
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
}
