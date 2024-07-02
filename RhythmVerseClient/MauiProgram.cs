using Microsoft.Extensions.Logging;
using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;
using SettingsManager;
using System.Text.Json;
using CommunityToolkit.Maui;
using RhythmVerseClient.Pages;
using RhythmVerseClient.Utilities;

namespace RhythmVerseClient
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif
            

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
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddSingleton<DownloadViewModel>();
            builder.Services.AddSingleton<CloneHeroViewModel>();
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<DownloadPage>();
            builder.Services.AddTransient<CloneHeroPage>();


            return builder.Build();
        }
    }
}
