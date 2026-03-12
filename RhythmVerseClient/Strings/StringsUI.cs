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
        public string OnyxImport { get; } = "Onyx importing '%filename%'...\n";
        public string OnyxBuild { get; } = "Clone Hero Song build started...\n";
        public string OnyxFinish { get; } = "Onyx finishing...\n";
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
        public string UploadCloud { get; } = "Google Drive";
        public string Install { get; } = "Install";

    }

    public class CloneHeroPageStrings : StringsUI
    {
        public string Title { get; } = "Clone Hero Song Library";
        public string Select { get; } = "Select";
        public string DisplayName { get; } = "Display Name";
        public string FileType { get; } = "File Type";
        public string FileSize { get; } = "File Size";

    }

    public class RhythmVersePageStrings : StringsUI
    {
        public string Title { get; } = "RhythmVerse";
        public string FilterTitle { get; } = "Filters";
        public List<string> Filters { get; } = new List<string> { "Artist", "Downloads", "Song Length", "Title" };
        public List<string> Orders { get; } = new List<string> { "Ascending", "Descending" };
        public string ApplyFilter { get; } = " ";
        public string ShowFilters { get; } = " ";
        public string ShowDownloads { get; } = " ";
        public string SortBy { get; } = "󰈍 ";
        public string Order { get; } = "󰒺 ";
        public string Instrument { get; } = "󰋄   󰍰 ";
        public string Difficulty { get; } = "Difficulty:";
        public string SearchText { get; } = "Search text...";
        public string PageSeparator { get; } = " / ";
        public string Total { get; } = " |  Total: ";
        public string Results { get; } = " Results: ";
        public string Refresh { get; } = " ";
        public string Artist { get; } = "󰠃 ";
        public string Album { get; } = " ";
        public string SongTitle { get; } = "󰝚";
        public string Time { get; } = " ";
        public string Released { get; } = " ";
        public string Genre { get; } = " ";
        public string Releases { get; } = " ";
        public string Roles { get; } = " ";
        public string Downloads { get; } = " ";
        public string Comments { get; } = " ";
        public string NoResults { get; } = "No Results...  So empty";
        public string LoadingResults { get; } = "Loading Results...";

    }
}
