using SettingsManager;

namespace RhythmVerseClient.Utilities
{
    public class Initializer
    {
        public Initializer() { }

        public static async Task InitializeAsync()
        {
            var TempDir = Path.Combine(Path.GetTempPath(), "RhythmVerseClient");
            var DownloadDir = Path.Combine(TempDir, "Downloads");
            var StagingDir = Path.Combine(TempDir, "Staging");
            var OutputDir = Path.Combine(TempDir, "Output");
            var CloneHeroDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clonehero");
            var CloneHeroSongsDir = Path.Combine(CloneHeroDataDir, "Songs");
        }

    }

    public class AppGlobalSettings
    {
        private static string GetDefaultCloneHeroDataDirectory()
        {
            if (OperatingSystem.IsAndroid())
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloneHero");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clonehero");
        }

        private readonly ISettingsManager<AppSettings> _settingsManager;
        private readonly AppSettings _appSettings;

        public bool UseMockData
        {
            get => _appSettings.UseMockData;
            set { _appSettings.UseMockData = value; _settingsManager.Save(); }
        }
        public string TempDir
        {
            get => _appSettings.TempDirectory ?? String.Empty;
            set { _appSettings.TempDirectory = value; _settingsManager.Save(); }
        }

        public string StagingDir
        {
            get => _appSettings.StagingDirectory ?? String.Empty;
            set { _appSettings.StagingDirectory = value; _settingsManager.Save(); }
        }

        public string DownloadDir
        {
            get => _appSettings.DownloadDirectory ?? String.Empty;
            set { _appSettings.DownloadDirectory = value; _settingsManager.Save(); }
        }

        public string OutputDir
        {
            get => _appSettings.OutputDirectory ?? String.Empty;
            set { _appSettings.OutputDirectory = value; _settingsManager.Save(); }
        }

        public string CloneHeroDataDir
        {
            get => _appSettings.CloneHeroDataDirectory ?? String.Empty;
            set { _appSettings.CloneHeroDataDirectory = value; _settingsManager.Save(); }
        }

        public string CloneHeroSongsDir
        {
            get => _appSettings.CloneHeroSongDirectory ?? String.Empty;
            set { _appSettings.CloneHeroSongDirectory = value; _settingsManager.Save(); }
        }

        public AppGlobalSettings(ISettingsManager<AppSettings> settingsManager)
        {
            _settingsManager = settingsManager;

            _appSettings = settingsManager.Settings;

            if (TempDir == "first_install" || TempDir == String.Empty)
            {
                TempDir = Path.Combine(Path.GetTempPath(), "RhythmVerseClient");
            }

            FileTools.CreateDirectoryIfNotExists(TempDir);

            if (DownloadDir == "first_install" || DownloadDir == String.Empty)
            {
                DownloadDir = Path.Combine(TempDir, "Downloads");
            }

            FileTools.CreateDirectoryIfNotExists(DownloadDir);

            if (StagingDir == "first_install" || StagingDir == String.Empty)
            {
                StagingDir = Path.Combine(TempDir, "Staging");
            }

            FileTools.CreateDirectoryIfNotExists(StagingDir);

            if (OutputDir == "first_install" || OutputDir == String.Empty)
            {
                OutputDir = Path.Combine(TempDir, "Output");
            }

            FileTools.CreateDirectoryIfNotExists(OutputDir);

            if (CloneHeroDataDir == "first_install" || CloneHeroDataDir == String.Empty)
            {
                CloneHeroDataDir = GetDefaultCloneHeroDataDirectory();
            }

            FileTools.CreateDirectoryIfNotExists(CloneHeroDataDir);

            if (CloneHeroSongsDir == "first_install" || CloneHeroSongsDir == String.Empty)
            {
                CloneHeroSongsDir = Path.Combine(CloneHeroDataDir, "Songs");
            }
            FileTools.CreateDirectoryIfNotExists(CloneHeroSongsDir);
        }


    }
}
