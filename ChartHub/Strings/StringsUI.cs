using ChartHub.Localization;

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
    public string Home => UiLocalization.Get("Main.Home");
    public string CloneHero => UiLocalization.Get("Main.CloneHero");
    public string RhythmVerse => UiLocalization.Get("Main.RhythmVerse");
    public string Encore => UiLocalization.Get("Main.Encore");
    public string Downloads => UiLocalization.Get("Main.Downloads");
    public string DesktopEntry => UiLocalization.Get("Main.DesktopEntry");
    public string Volume => UiLocalization.Get("Main.Volume");
    public string InstallSongs => UiLocalization.Get("Main.InstallSongs");
    public string Settings => UiLocalization.Get("Main.Settings");
    public string Cancel => UiIcons.Cancel;
    public string FilterSectionRhythmVerse => UiLocalization.Get("Main.FilterSectionRhythmVerse");
    public string FilterSectionEncore => UiLocalization.Get("Main.FilterSectionEncore");
    public string FilterFallbackTitle => UiLocalization.Get("Main.FilterFallbackTitle");
    public string FilterFallbackMessage => UiLocalization.Get("Main.FilterFallbackMessage");
    public string FilterFallbackMessageAndroid => UiLocalization.Get("Main.FilterFallbackMessageAndroid");
    public string NavSectionTitle => UiLocalization.Get("Main.NavSectionTitle");
    public string BackToNavigation => UiLocalization.Get("Main.BackToNavigation");
    public string FiltersButton => UiLocalization.Get("Main.FiltersButton");
    public string MenuButton => UiLocalization.Get("Main.MenuButton");
    public string Input => UiLocalization.Get("Main.Input");
    public string Controller => UiLocalization.Get("Main.Controller");
    public string Mouse => UiLocalization.Get("Main.Mouse");
    public string Keyboard => UiLocalization.Get("Main.Keyboard");
}

public class InstallPageStrings : StringsUI
{
    public string Title => UiLocalization.Get("Install.Title");
    public string StartButton => UiLocalization.Get("Install.StartButton");
    public string GoBack => UiLocalization.Get("Install.GoBack");
    public string StartProcess => UiLocalization.Get("Install.StartProcess");
    public string Done => UiLocalization.Get("Install.Done");
    public string UnzipFile => UiLocalization.Get("Install.UnzipFile");
    public string ExtractFile => UiLocalization.Get("Install.ExtractFile");
    public string UnRarFile => UiLocalization.Get("Install.UnRarFile");
    public string OnyxImport => UiLocalization.Get("Install.OnyxImport");
    public string OnyxBuild => UiLocalization.Get("Install.OnyxBuild");
    public string OnyxFinish => UiLocalization.Get("Install.OnyxFinish");
    public string InstallSongs => UiLocalization.Get("Install.InstallSongs");
    public string Finished => UiLocalization.Get("Install.Finished");
}

public class DownloadPageStrings : StringsUI
{
    public string Title => UiLocalization.Get("Download.Title");
    public string DisplayName => UiLocalization.Get("Download.DisplayName");
    public string FileType => UiLocalization.Get("Download.FileType");
    public string FileSize => UiLocalization.Get("Download.FileSize");
    public string InstallSongs => UiLocalization.Get("Download.InstallSongs");
    public string UploadCloud => UiLocalization.Get("Download.UploadCloud");
    public string Install => UiLocalization.Get("Download.Install");
    public string DeleteSelected => UiLocalization.Get("Download.DeleteSelected");
    public string InstallProgressLabel => UiLocalization.Get("Download.InstallProgressLabel");
    public string ClearLog => UiLocalization.Get("Download.ClearLog");
    public string Cancel => UiLocalization.Get("Download.Cancel");
    public string Dismiss => UiLocalization.Get("Download.Dismiss");
    public string CopyAllLogs => UiLocalization.Get("Download.CopyAllLogs");
    public string CopyLog => UiLocalization.Get("Download.CopyLog");
    public string StateLabel => UiLocalization.Get("Download.StateLabel");
    public string SortLabel => UiLocalization.Get("Download.SortLabel");
    public string Descending => UiLocalization.Get("Download.Descending");
    public string EmptyState => UiLocalization.Get("Download.EmptyState");
    public string ExpandLog => UiLocalization.Get("Download.ExpandLog");
    public string CollapseLog => UiLocalization.Get("Download.CollapseLog");
    public string DeleteJob => UiLocalization.Get("Download.DeleteJob");
    public string ArtistLabel => UiLocalization.Get("Download.ArtistLabel");
    public string CharterLabel => UiLocalization.Get("Download.CharterLabel");

}

public class CloneHeroPageStrings : StringsUI
{
    public string Title => UiLocalization.Get("CloneHero.Title");
    public string PageHeader => UiLocalization.Get("CloneHero.PageHeader");
    public string RefreshLibrary => UiLocalization.Get("CloneHero.RefreshLibrary");
    public string RestoreLastDeleted => UiLocalization.Get("CloneHero.RestoreLastDeleted");
    public string PreparingLibrary => UiLocalization.Get("CloneHero.PreparingLibrary");
    public string Artists => UiLocalization.Get("CloneHero.Artists");
    public string ColumnTitle => UiLocalization.Get("CloneHero.ColumnTitle");
    public string ColumnArtist => UiLocalization.Get("CloneHero.ColumnArtist");
    public string ColumnCharter => UiLocalization.Get("CloneHero.ColumnCharter");
    public string ColumnSource => UiLocalization.Get("CloneHero.ColumnSource");
    public string DeleteSong => UiLocalization.Get("CloneHero.DeleteSong");
    public string Back => UiLocalization.Get("CloneHero.Back");
    public string Select => UiLocalization.Get("CloneHero.Select");
    public string DisplayName => UiLocalization.Get("CloneHero.DisplayName");
    public string FileType => UiLocalization.Get("CloneHero.FileType");
    public string FileSize => UiLocalization.Get("CloneHero.FileSize");

}

public class SongSourcePageStrings : StringsUI
{
    public string Refresh => UiIcons.Refresh;
    public string Artist => UiIcons.Artist;
    public string Album => UiIcons.Album;
    public string SongTitle => UiIcons.SongTitle;
    public string Time => UiIcons.Time;
    public string Released => UiIcons.Released;
    public string Genre => UiIcons.Genre;
    public string Releases => UiIcons.Releases;
    public string Roles => UiIcons.Roles;
    public string Downloads => UiIcons.Downloads;
    public string Comments => UiIcons.Comments;
    public string NoResults => UiLocalization.Get("SongSource.NoResults");
    public string LoadingResults => UiLocalization.Get("SongSource.LoadingResults");
    public List<string> SortColumns { get; } = ["name", "artist", "album", "genre", "year", "charter", "length", "modifiedTime"];
    public List<string> SortDirections { get; } = ["asc", "desc"];
    public virtual string Title => UiLocalization.Get("SongSource.Title");
    public virtual string SearchText => UiLocalization.Get("SongSource.SearchText");
    public string InLibrary => UiLocalization.Get("SongSource.InLibrary");
}

public class RhythmVersePageStrings : SongSourcePageStrings
{
    public string ApplyFilter => UiIcons.ApplyFilter;
    public string ShowFilters => UiIcons.ShowFilters;
    public string Download => UiIcons.Downloads;
    public string SortBy => UiIcons.SortBy;
    public string Order => UiIcons.Order;
    public string Instrument => UiIcons.Instrument;
    public override string Title => UiLocalization.Get("RhythmVerse.Title");
    public string FilterTitle => UiLocalization.Get("RhythmVerse.FilterTitle");
    public string FilterSectionTitle => UiLocalization.Get("RhythmVerse.FilterSectionTitle");
    public string ApplyFilters => UiLocalization.Get("RhythmVerse.ApplyFilters");
    public List<string> Filters { get; } =
    [
        UiLocalization.Get("RhythmVerse.FilterArtist"),
        UiLocalization.Get("RhythmVerse.FilterDownloads"),
        UiLocalization.Get("RhythmVerse.FilterSongLength"),
        UiLocalization.Get("RhythmVerse.FilterTitleText"),
        UiLocalization.Get("RhythmVerse.FilterUpdated"),
    ];
    public List<string> Orders { get; } = [UiLocalization.Get("RhythmVerse.OrderAscending"), UiLocalization.Get("RhythmVerse.OrderDescending")];
    public string Difficulty => UiLocalization.Get("RhythmVerse.Difficulty");
    public override string SearchText => UiLocalization.Get("RhythmVerse.SearchText");
    public string Total => UiLocalization.Get("RhythmVerse.Total");
}

public class EncorePageStrings : SongSourcePageStrings
{
    public override string Title => UiLocalization.Get("Encore.Title");
    public override string SearchText => UiLocalization.Get("Encore.SearchText");
    public string ToggleAdvanced => UiIcons.ShowFilters;
    public string Download => UiIcons.Downloads;
    public string SortBy => UiIcons.SortBy;
    public string Order => UiIcons.Order;
    public string Instrument => UiIcons.Instrument;
    public string FilterSectionTitle => UiLocalization.Get("Encore.FilterSectionTitle");
    public string FilterLabelInstrument => UiLocalization.Get("Encore.FilterLabelInstrument");
    public string FilterLabelDifficulty => UiLocalization.Get("Encore.FilterLabelDifficulty");
    public string FilterLabelDrumType => UiLocalization.Get("Encore.FilterLabelDrumType");
    public string FilterLabelSortColumn => UiLocalization.Get("Encore.FilterLabelSortColumn");
    public string FilterLabelSortDirection => UiLocalization.Get("Encore.FilterLabelSortDirection");
    public string LabelAdvancedName => UiLocalization.Get("Encore.LabelAdvancedName");
    public string PlaceholderName => UiLocalization.Get("Encore.PlaceholderName");
    public string LabelAdvancedArtist => UiLocalization.Get("Encore.LabelAdvancedArtist");
    public string PlaceholderArtist => UiLocalization.Get("Encore.PlaceholderArtist");
    public string LabelAdvancedCharter => UiLocalization.Get("Encore.LabelAdvancedCharter");
    public string PlaceholderCharter => UiLocalization.Get("Encore.PlaceholderCharter");
    public string LabelAdvancedAlbum => UiLocalization.Get("Encore.LabelAdvancedAlbum");
    public string PlaceholderAlbum => UiLocalization.Get("Encore.PlaceholderAlbum");
    public string LabelAdvancedGenre => UiLocalization.Get("Encore.LabelAdvancedGenre");
    public string PlaceholderGenre => UiLocalization.Get("Encore.PlaceholderGenre");
    public string LabelAdvancedYear => UiLocalization.Get("Encore.LabelAdvancedYear");
    public string PlaceholderYear => UiLocalization.Get("Encore.PlaceholderYear");
    public string LabelMinYear => UiLocalization.Get("Encore.LabelMinYear");
    public string PlaceholderMinYear => UiLocalization.Get("Encore.PlaceholderMinYear");
    public string LabelMaxYear => UiLocalization.Get("Encore.LabelMaxYear");
    public string PlaceholderMaxYear => UiLocalization.Get("Encore.PlaceholderMaxYear");
    public string LabelMinLength => UiLocalization.Get("Encore.LabelMinLength");
    public string PlaceholderMinLength => UiLocalization.Get("Encore.PlaceholderMinLength");
    public string LabelMaxLength => UiLocalization.Get("Encore.LabelMaxLength");
    public string PlaceholderMaxLength => UiLocalization.Get("Encore.PlaceholderMaxLength");
    public string LabelDrumsReviewed => UiLocalization.Get("Encore.LabelDrumsReviewed");
    public string CheckDrumsReviewed => UiLocalization.Get("Encore.CheckDrumsReviewed");
    public string LabelHasVideo => UiLocalization.Get("Encore.LabelHasVideo");
    public string CheckHasVideo => UiLocalization.Get("Encore.CheckHasVideo");
    public string LabelHasLyrics => UiLocalization.Get("Encore.LabelHasLyrics");
    public string CheckHasLyrics => UiLocalization.Get("Encore.CheckHasLyrics");
    public string LabelHasVocals => UiLocalization.Get("Encore.LabelHasVocals");
    public string CheckHasVocals => UiLocalization.Get("Encore.CheckHasVocals");
    public string Label2xKick => UiLocalization.Get("Encore.Label2xKick");
    public string Check2xKick => UiLocalization.Get("Encore.Check2xKick");
    public string LabelHasIssues => UiLocalization.Get("Encore.LabelHasIssues");
    public string CheckHasIssues => UiLocalization.Get("Encore.CheckHasIssues");
    public string LabelModchart => UiLocalization.Get("Encore.LabelModchart");
    public string CheckModchart => UiLocalization.Get("Encore.CheckModchart");
    public string ApplyFilters => UiLocalization.Get("Encore.ApplyFilters");
}

public class DesktopEntryPageStrings : StringsUI
{
    public string PageHeader => UiLocalization.Get("DesktopEntry.PageHeader");
    public string Refresh => UiLocalization.Get("DesktopEntry.Refresh");
    public string NoEntries => UiLocalization.Get("DesktopEntry.NoEntries");
    public string Execute => UiLocalization.Get("DesktopEntry.Execute");
    public string Kill => UiLocalization.Get("DesktopEntry.Kill");
}

public class SettingsPageStrings : StringsUI
{
    public string AuthTitle => UiLocalization.Get("Settings.AuthTitle");
    public string AuthStatusLabel => UiLocalization.Get("Settings.AuthStatusLabel");
    public string AuthenticateGoogle => UiLocalization.Get("Settings.AuthenticateGoogle");
    public string Working => UiLocalization.Get("Settings.Working");
    public string ValidationBanner => UiLocalization.Get("Settings.ValidationBanner");
    public string RestartBanner => UiLocalization.Get("Settings.RestartBanner");
    public string DeveloperSettingsLabel => UiLocalization.Get("Settings.DeveloperSettingsLabel");
    public string SecretsTitle => UiLocalization.Get("Settings.SecretsTitle");
    public string SecretsDescription => UiLocalization.Get("Settings.SecretsDescription");
    public string ProviderConfiguration => UiLocalization.Get("Settings.ProviderConfiguration");
    public string SecretValuePlaceholder => UiLocalization.Get("Settings.SecretValuePlaceholder");
    public string SecretSaveButton => UiLocalization.Get("Settings.SecretSaveButton");
    public string SecretClearButton => UiLocalization.Get("Settings.SecretClearButton");
    public string ServerSetupTitle => UiLocalization.Get("Settings.ServerSetupTitle");
    public string ServerSetupDescription => UiLocalization.Get("Settings.ServerSetupDescription");
    public string ServerBaseUrlLabel => UiLocalization.Get("Settings.ServerBaseUrlLabel");
    public string ServerBaseUrlPlaceholder => UiLocalization.Get("Settings.ServerBaseUrlPlaceholder");
    public string TestConnectionButton => UiLocalization.Get("Settings.TestConnectionButton");
    public string ClearAuthButton => UiLocalization.Get("Settings.ClearAuthButton");
    public string TokenDiagnosticLabel => UiLocalization.Get("Settings.TokenDiagnosticLabel");
    public string TokenNotSet => UiLocalization.Get("Settings.TokenNotSet");
    public string GeneralSettingsTitle => UiLocalization.Get("Settings.GeneralSettingsTitle");
}

public class VolumePageStrings : StringsUI
{
    public string PageHeader => UiLocalization.Get("Volume.PageHeader");
    public string PageDescription => UiLocalization.Get("Volume.PageDescription");
    public string Refresh => UiLocalization.Get("Volume.Refresh");
    public string AndroidHardwareBindingLabel => UiLocalization.Get("Volume.AndroidHardwareBindingLabel");
    public string MasterVolumeTitle => UiLocalization.Get("Volume.MasterVolumeTitle");
    public string ApplyMaster => UiLocalization.Get("Volume.ApplyMaster");
    public string SessionsTitle => UiLocalization.Get("Volume.SessionsTitle");
    public string NoActiveSessions => UiLocalization.Get("Volume.NoActiveSessions");
    public string ApplySession => UiLocalization.Get("Volume.ApplySession");
    public string FormatPendingPercent(double value) => UiLocalization.Format("Volume.PendingPercent", Math.Round(value));
}

public class InputPageStrings : StringsUI
{
    public string ControllerStatusConnected => UiLocalization.Get("Input.Controller.Status.Connected");
    public string ControllerStatusDisconnected => UiLocalization.Get("Input.Controller.Status.Disconnected");
    public string TouchpadStatusConnected => UiLocalization.Get("Input.Touchpad.Status.Connected");
    public string TouchpadStatusDisconnected => UiLocalization.Get("Input.Touchpad.Status.Disconnected");
    public string KeyboardStatusConnected => UiLocalization.Get("Input.Keyboard.Status.Connected");
    public string KeyboardStatusDisconnected => UiLocalization.Get("Input.Keyboard.Status.Disconnected");
    public string KeyEnter => UiLocalization.Get("Input.Keyboard.Key.Enter");
    public string KeyBackspace => UiLocalization.Get("Input.Keyboard.Key.Backspace");
    public string KeyEscape => UiLocalization.Get("Input.Keyboard.Key.Escape");
    public string KeyTab => UiLocalization.Get("Input.Keyboard.Key.Tab");
    public string KeyboardHint => UiLocalization.Get("Input.Keyboard.Hint");
    public string Controller => UiLocalization.Get("Main.Controller");
    public string Mouse => UiLocalization.Get("Main.Mouse");
    public string Keyboard => UiLocalization.Get("Main.Keyboard");
}
