using RhythmVerseClient.Services;
using RhythmVerseClient.Utilities;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace RhythmVerseClient
{
    public partial class App : Application
    {
        private AppGlobalSettings _globalSettings;
        public const string ZIP_FILE_URL = "https://calahil.github.io/nautilus.zip";

        public static readonly string ZipFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nautilus.zip");

        public App(IServiceProvider serviceProvider, AppGlobalSettings settings)
        {
            InitializeComponent();

            Current.UserAppTheme = AppTheme.Dark;
            _globalSettings = settings;
            MainPage = serviceProvider.GetRequiredService<MainPage>();

        }

        protected async override void OnStart()
        {
            base.OnStart();
            var nautilisEXE = Path.Combine(_globalSettings.NautilusDirectoryPath, "Nautilus.exe");
            if (!File.Exists(nautilisEXE))
                    {
                if (!File.Exists(ZipFilePath))
                {
                    HttpClient client = new();

                    byte[] data = await client.GetByteArrayAsync(ZIP_FILE_URL);
                    await File.WriteAllBytesAsync(ZipFilePath, data);

                    if (!Directory.Exists(_globalSettings.NautilusDirectoryPath))
                    {
                        Directory.CreateDirectory(_globalSettings.RhythmverseAppPath);
                    }

                    using var archive = ArchiveFactory.Open(ZipFilePath);
                    foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                    {
                        entry.WriteToDirectory(_globalSettings.RhythmverseAppPath, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }

                if (File.Exists(ZipFilePath) && File.Exists(nautilisEXE))
                {
                    File.Delete(ZipFilePath);
                }

                Toolbox.CreateDirectoryIfNotExists(_globalSettings.PhaseshiftDir);
                Toolbox.CreateDirectoryIfNotExists(_globalSettings.PhaseshiftMusicDir);
                Toolbox.CreateDirectoryIfNotExists(_globalSettings.DownloadDir);
            }
        }

    }
}
