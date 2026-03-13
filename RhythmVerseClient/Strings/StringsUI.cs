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
        public string ApplyFilter { get; } = "avares://RhythmVerseClient/Resources/Images/check24dp.png";
        public string ShowFilters { get; } = "avares://RhythmVerseClient/Resources/Images/filteralt24dp.png";
        public string ShowDownloads { get; } = "avares://RhythmVerseClient/Resources/Images/download24dp.png";
        public string SortBy { get; } = "avares://RhythmVerseClient/Resources/Images/sortbyalpha24dp.png";
        public string Order { get; } = "avares://RhythmVerseClient/Resources/Images/sort24dp.png";
        public string Instrument { get; } = "avares://RhythmVerseClient/Resources/Images/joystick24dp.png";
        public string Difficulty { get; } = "Difficulty:";
        public string SearchText { get; } = "Search text...";
        public string PageSeparator { get; } = " / ";
        public string Total { get; } = " |  Total: ";
        public string Results { get; } = " Results: ";
        public string Refresh { get; } = "avares://RhythmVerseClient/Resources/Images/refresh24dp.png";
        public string Artist { get; } = "avares://RhythmVerseClient/Resources/Images/artist24dp.png";
        public string Album { get; } = "avares://RhythmVerseClient/Resources/Images/album24dp.png";
        public string SongTitle { get; } = "avares://RhythmVerseClient/Resources/Images/music_note24dp.png";
        public string Time { get; } = "avares://RhythmVerseClient/Resources/Images/schedule24dp.png";
        public string Released { get; } = "avares://RhythmVerseClient/Resources/Images/calendaraddon24dp.png";
        public string Genre { get; } = "avares://RhythmVerseClient/Resources/Images/genres24dp.png";
        public string Releases { get; } = "avares://RhythmVerseClient/Resources/Images/homestorage24dp.png";
        public string Roles { get; } = "avares://RhythmVerseClient/Resources/Images/group24dp.png";
        public string Downloads { get; } = "avares://RhythmVerseClient/Resources/Images/download24dp.png";
        public string Comments { get; } = "avares://RhythmVerseClient/Resources/Images/comment24dp.png";
        public string NoResults { get; } = "No Results...  So empty";
        public string LoadingResults { get; } = "Loading Results...";

    }
}
