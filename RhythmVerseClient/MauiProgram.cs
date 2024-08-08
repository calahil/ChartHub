using CommunityToolkit.Maui;
using FFImageLoading.Maui;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;
using RhythmVerseClient.Pages;
using RhythmVerseClient.Services;
using RhythmVerseClient.Utilities;
using RhythmVerseClient.ViewModels;
using SettingsManager;
using Syncfusion.Maui.Core.Hosting;
using System.Text.Json;
using WinUIEx;

namespace RhythmVerseClient
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseFFImageLoading()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("fa-solid-900.tff", "FontAwesome");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif
            builder.Configuration.AddUserSecrets<App>();
            
            var settingsFileName = "appsettings.json";
            var sourceFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, settingsFileName);
            var destinationFilePath = Path.Combine(FileSystem.AppDataDirectory, settingsFileName);

            if (!File.Exists(destinationFilePath))
            {
                File.Copy(sourceFilePath, destinationFilePath);
            }

            string json = File.ReadAllText(destinationFilePath);

            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonCerealOptions.Instance)
                    ?? throw new JsonException("Failed to deserialize settings.");


            builder.Services.AddSingleton(settings);

            builder.Services.AddSingleton<ISettingsManager<AppSettings>>(serviceProvider =>
            {
                var settingsFilePath = Path.Combine(FileSystem.AppDataDirectory, settingsFileName);
                var appSettings = serviceProvider.GetRequiredService<AppSettings>();
                return new SettingsManager<AppSettings>(settingsFilePath, appSettings);
            });

            builder.Services.AddSingleton<IKeystrokeSender, WindowsKeystrokeSender>();
            builder.Services.AddSingleton<AppGlobalSettings>();
            builder.Services.AddSingleton<IGoogleDriveClient, GoogleDriveClient>();
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddSingleton<DownloadViewModel>();
            builder.Services.AddSingleton<CloneHeroViewModel>();
            builder.Services.AddSingleton<InstallSongViewModel>();
            builder.Services.AddSingleton<RhythmVerseModel>();
            builder.Services.AddSingleton<DownloadPage>();
            builder.Services.AddSingleton<CloneHeroPage>();
            builder.Services.AddSingleton<InstallSongPage>();
            builder.Services.AddSingleton<RhythmVersePage>();
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddSingleton<DriveService>();
            builder.Services.AddSingleton<Initializer>();

            builder.ConfigureSyncfusionCore();

#if WINDOWS
            builder.ConfigureLifecycleEvents(events =>
            {
                events.AddWindows(wndLifeCycleBuilder =>
                {
                    wndLifeCycleBuilder.OnWindowCreated(window =>
                    {
                        window.Maximize();
                    });
                });
            });
#endif

            return builder.Build();
        }
    }
}
