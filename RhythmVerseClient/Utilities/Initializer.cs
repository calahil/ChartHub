using SharpCompress.Archives;
using SharpCompress.Common;

namespace RhythmVerseClient.Utilities
{
    public class Initializer
    {
        public Initializer() { }

        public static async Task InitializeAsync()
        {
            var NautilusDirectoryPath = Toolbox.ConstructPath(FileSystem.Current.AppDataDirectory, "nautilus");
            var PhaseshiftDir = Toolbox.ConstructPath(NautilusDirectoryPath, "phaseshift");
            var PhaseshiftMusicDir = Toolbox.ConstructPath(PhaseshiftDir, "Music");
            var DownloadDir = Toolbox.ConstructPath(PhaseshiftDir, "downloads");
            var DownloadStaging = Toolbox.ConstructPath(PhaseshiftDir, "staging");
            var CloneHeroDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Clone Hero");
            var CloneHeroSongsDir = Toolbox.ConstructPath(CloneHeroDataDir, "Songs");
            Toolbox.CreateDirectoryIfNotExists(PhaseshiftDir);
            Toolbox.CreateDirectoryIfNotExists(PhaseshiftMusicDir);
            Toolbox.CreateDirectoryIfNotExists(DownloadDir);
            Toolbox.CreateDirectoryIfNotExists(DownloadStaging);
        }

    }
}
