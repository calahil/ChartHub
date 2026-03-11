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
        public string ApplyFilter { get; } = "Apply Filters";
        public string ShowFilters { get; } = "Show Filters";
        public string HideFilters { get; } = "Hide Filters";
        public string ShowDownloads { get; } = "Show Downloads";
        public string HideDownloads { get; } = "Hide Downloads";
        public string SortBy { get; } = "Sort By:";
        public string Order { get; } = "Order:";
        public string Instrument { get; } = "Instrument:";
        public string Difficulty { get; } = "Difficulty:";
        public string SearchText { get; } = "Search text...";
        public string PageSeparator { get; } = " / ";
        public string Total { get; } = " |  Total: ";
        public string Results { get; } = " Results: ";
        public string Refresh { get; } = "Refresh";
        public string Artist { get; } = "Artist:";
        public string Album { get; } = "Album:";
        public string SongTitle { get; } = "Title:";
        public string Time { get; } = "Time:";
        public string Released { get; } = "Released:";
        public string Genre { get; } = "Genre:";
        public string Releases { get; } = "Releases:";
        public string Roles { get; } = "Roles:";
        public string Downloads { get; } = "Downloads:";
        public string Comments { get; } = "Comments:";
        public string NoResults { get; } = "No Results...  So empty";
        public string LoadingResults { get; } = "Loading Results...";

    }
}
