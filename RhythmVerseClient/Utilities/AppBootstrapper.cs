using System.Text.Json;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RhythmVerseClient.Services;
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

            var serviceProvider = services.BuildServiceProvider();
            try
            {
                serviceProvider.GetRequiredService<IGoogleDriveClient>().InitializeAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Logger.LogMessage($"Google Drive initialization failed during bootstrap: {ex.Message}");
            }
            return serviceProvider;
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RhythmVerseClient");
            FileTools.CreateDirectoryIfNotExists(configDir);

            // Build configuration with user secrets
            var configBuilder = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(configDir, "appsettings.json"), optional: true)
                .AddUserSecrets<UserSecretsAnchor>(optional: true);

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
    }
}
