using Microsoft.Extensions.Logging;
using RhythmVerseClient.Services;
using SettingsManager;

namespace RhythmVerseClient
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif
            builder.Services.AddSingleton(new AppSettings());

            // Register the SettingsManager as a singleton
            builder.Services.AddSingleton<ISettingsManager<AppSettings>>(serviceProvider =>
            {
                var settings = serviceProvider.GetRequiredService<AppSettings>();
                var settingsFilePath = Path.Combine(FileSystem.AppDataDirectory, "appsettings.json");
                return new SettingsManager<AppSettings>(settings, settingsFilePath);
            });
            builder.Services.AddSingleton<IKeystrokeSender, WindowsKeystrokeSender>();

            return builder.Build();
        }
    }
}
