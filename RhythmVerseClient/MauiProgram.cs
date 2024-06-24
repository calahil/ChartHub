using Microsoft.Extensions.Logging;
using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;
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
            builder.Services.AddSingleton<SettingsManager<AppSettings>>(serviceProvider =>
            {
                var settings = serviceProvider.GetRequiredService<AppSettings>();
                var settingsFilePath = Path.Combine(FileSystem.AppDataDirectory, "appsettings.json");
                return new SettingsManager<AppSettings>(settings, settingsFilePath);
            });
            builder.Services.AddSingleton<IKeystrokeSender, WindowsKeystrokeSender>();
            builder.Services.AddSingleton<IFileSystemManager, FileSystemManager>();
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddTransient<MainPage>();

            return builder.Build();
        }
    }
}
