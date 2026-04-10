namespace ChartHub.Strings;

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

public class MainViewPageStrings : StringsUI
{
    public string Home { get; } = "Home";
    public string CloneHero { get; } = "Clone Hero";
    public string RhythmVerse { get; } = "RhythmVerse";
    public string Encore { get; } = "Encore";
    public string Downloads { get; } = "Downloads";
    public string InstallSongs { get; } = "Install Songs";
    public string Settings { get; } = "Settings";
    public string Cancel { get; } = "avares://ChartHub/Resources/Svg/cancel_24dp.svg";
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

public class SongSourcePageStrings : StringsUI
{
    public string Refresh { get; } = "avares://ChartHub/Resources/Svg/refresh_24dp.svg";
    public string Artist { get; } = "avares://ChartHub/Resources/Svg/artist_24dp.svg";
    public string Album { get; } = "avares://ChartHub/Resources/Svg/album_24dp.svg";
    public string SongTitle { get; } = "avares://ChartHub/Resources/Svg/music_note_2_24dp.svg";
    public string Time { get; } = "avares://ChartHub/Resources/Svg/schedule_24dp.svg";
    public string Released { get; } = "avares://ChartHub/Resources/Svg/calendar_add_on_24dp.svg";
    public string Genre { get; } = "avares://ChartHub/Resources/Svg/genres_24dp.svg";
    public string Releases { get; } = "avares://ChartHub/Resources/Svg/home_storage_24dp.svg";
    public string Roles { get; } = "avares://ChartHub/Resources/Svg/group_24dp.svg";
    public string Downloads { get; } = "avares://ChartHub/Resources/Svg/download_24dp.svg";
    public string Comments { get; } = "avares://ChartHub/Resources/Svg/comment_24dp.svg";
    public string NoResults { get; } = "No Results...  So empty";
    public string LoadingResults { get; } = "Loading Results...";
    public List<string> SortColumns { get; } = ["name", "artist", "album", "genre", "year", "charter", "length", "modifiedTime"];
    public List<string> SortDirections { get; } = ["asc", "desc"];
    public virtual string Title { get; } = "Song Source";
    public virtual string SearchText { get; } = "Song Source Search...";
    public string InLibrary { get; } = "In Library";
}

public class RhythmVersePageStrings : SongSourcePageStrings
{
    public string ApplyFilter { get; } = "avares://ChartHub/Resources/Svg/check_24dp.svg";
    public string ShowFilters { get; } = "avares://ChartHub/Resources/Svg/filter_alt_24dp.svg";
    public string Download { get; } = "avares://ChartHub/Resources/Svg/download_24dp.svg";
    public string SortBy { get; } = "avares://ChartHub/Resources/Svg/sort_by_alpha_24dp.svg";
    public string Order { get; } = "avares://ChartHub/Resources/Svg/sort_24dp.svg";
    public string Instrument { get; } = "avares://ChartHub/Resources/Svg/joystick_24dp.svg";
    public override string Title { get; } = "RhythmVerse";
    public string FilterTitle { get; } = "Filters";
    public List<string> Filters { get; } = new List<string> { "Artist", "Downloads", "Song Length", "Title" };
    public List<string> Orders { get; } = new List<string> { "Ascending", "Descending" };
    public string Difficulty { get; } = "Difficulty:";
    public override string SearchText { get; } = "Search RhythmVerse charts...";
    public string Total { get; } = "Total: ";
}

public class EncorePageStrings : SongSourcePageStrings
{
    public override string Title { get; } = "Chorus Encore";
    public override string SearchText { get; } = "Search Encore charts...";
    public string ToggleAdvanced { get; } = "avares://ChartHub/Resources/Svg/filter_alt_24dp.svg";
    public string Download { get; } = "avares://ChartHub/Resources/Svg/download_24dp.svg";
    public string SortBy { get; } = "avares://ChartHub/Resources/Svg/sort_by_alpha_24dp.svg";
    public string Order { get; } = "avares://ChartHub/Resources/Svg/sort_24dp.svg";
    public string Instrument { get; } = "avares://ChartHub/Resources/Svg/joystick_24dp.svg";
}
