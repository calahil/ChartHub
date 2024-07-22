namespace RhythmVerseClient.Strings
{
    public static class StringExtensions
    {
        public static string FormatString(this string str, string newText)
        {
            return str.Replace("%filename%", newText);
        }
    }

    public class StringsUI
    {

    }

    public class InstallPageStrings : StringsUI
    {
        public string Title { get; } = "Install Songs Page";
        public string StartButton { get; } = "Start Progress";
        public string GoBack { get; } = "Go Back";
        public string StartProcess { get; } = "Song install has begun...\n";
        public string Done { get; } = "Done\n";
        public string UnzipFile { get; } = "Begin unziping '%filename%' into staging directory...";
        public string ExtractFile { get; } = "Begin extracting '%filename%' into staging directory...";
        public string UnRarFile { get; } = "Begin unraring '%filename%' into staging directory...";
        public string RBConFile { get; } = "Moving '%filename%' into Nautilus directory...";
        public string StartNautilus { get; } = "Starting Nautilus...\n";
        public string NautilusConversion { get; } = "RB3 CON file conversion started...\n";
        public string StopNautilus { get; } = "Stopping Nautilus...\n";
        public string InstallSongs { get; } = "Installing songs to your Clone Hero song directory...\n";
        public string Finished { get; } = "Finished\n";
    }

    public class DownloadPageStrings : StringsUI
    {
        public string Title { get; } = "Downloads";
        public string DisplayName { get; } = "Display Name";
        public string FileType { get; } = "File Type";
        public string FileSize { get; } = "File Size";
        public string InstallSongs { get; } = "Install Songs";
        public string Install { get; } = "Install";

    }

    public class CloneHeroPageStrings : StringsUI
    {
        public string Title { get; } = "Clone Hero Song Library";
        public string DisplayName { get; } = "Display Name";
        public string FileType { get; } = "File Type";
        public string FileSize { get; } = "File Size";

    }
}
