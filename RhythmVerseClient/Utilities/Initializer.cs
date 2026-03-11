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
}
