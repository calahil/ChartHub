namespace RhythmVerseClient.Strings
{
    public class StringsUI
    {
        public string FormatString(string template, Dictionary<string, string> values)
        {
            foreach (var placeholder in values)
            {
                template = template.Replace($"%{placeholder.Key}%", placeholder.Value);
            }
            return template;
        }
    }

    public class InstallPageStrings : StringsUI
    {
        public string Title { get; } = "Install Songs Page";
        public string StartButton { get; } = "Start Progress";
        public string StartProcess { get; } = "Song install has begun...";
        public string Done { get; } = "Done";
        public string UnzipFile { get; } = "Begin unziping %filename% into staging directory...";
        public string ExtractFile { get; } = "Begin extracting %filename% into staging directory...";
        public string UnRarFile { get; } = "Begin unraring %filename% into staging directory...";
        public string RBConFile { get; } = "Moving %filename% into Nautilus directory...";
        public string StartNautilus { get; } = "Starting Nautilus...";
        public string NautilusConversion { get; } = "RB3 CON file conversion started...";
        public string StopNautilus { get; } = "Stopping Nautilus...";
        public string InstallSongs { get; } = "Installing songs to your Clone Hero song directory...";
    }

    public class DownloadPageStrings : StringsUI
    {
        public string Title { get; } = "Downloads";
        public string DisplayName { get; } = "Display Name";
        public string FileType { get; } = "File Type";
        public string FileSize { get; } = "File Size";
       
    }

    public class CloneHeroPageStrings : StringsUI
    {
        public string Title { get; } = "Clone Hero Song Library";
        public string DisplayName { get; } = "Display Name";
        public string FileType { get; } = "File Type";
        public string FileSize { get; } = "File Size";

    }
}
