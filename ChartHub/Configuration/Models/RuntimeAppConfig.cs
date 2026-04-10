using ChartHub.Configuration.Metadata;

namespace ChartHub.Configuration.Models;

public sealed class RuntimeAppConfig
{
    [SettingGroup("General")]
    [SettingDisplay("RhythmVerse Source")]
    [SettingEditor(SettingEditorKind.Dropdown)]
    [SettingHotReloadable(true)]
    public RhythmVerseSource RhythmVerseSource { get; set; } = RhythmVerseSource.RhythmVerseOfficial;

    [SettingGroup("General")]
    [SettingDisplay("Use Mock Data")]
    [SettingEditor(SettingEditorKind.Toggle)]
    [SettingHotReloadable(true)]
    public bool UseMockData { get; set; }

    [SettingGroup("Storage")]
    [SettingDisplay("Temp Directory")]
    [SettingEditor(SettingEditorKind.DirectoryPicker)]
    [SettingHotReloadable(false)]
    [SettingRequiresRestart]
    public string TempDirectory { get; set; } = "first_install";

    [SettingGroup("Storage")]
    [SettingDisplay("Download Directory")]
    [SettingEditor(SettingEditorKind.DirectoryPicker)]
    [SettingHotReloadable(true)]
    public string DownloadDirectory { get; set; } = "first_install";

    [SettingGroup("Storage")]
    [SettingDisplay("Staging Directory")]
    [SettingEditor(SettingEditorKind.DirectoryPicker)]
    [SettingHotReloadable(false)]
    [SettingRequiresRestart]
    public string StagingDirectory { get; set; } = "first_install";

    [SettingGroup("Storage")]
    [SettingDisplay("Output Directory")]
    [SettingEditor(SettingEditorKind.DirectoryPicker)]
    [SettingHotReloadable(false)]
    [SettingRequiresRestart]
    public string OutputDirectory { get; set; } = "first_install";

    [SettingGroup("Clone Hero")]
    [SettingDisplay("Clone Hero Song Directory")]
    [SettingEditor(SettingEditorKind.DirectoryPicker)]
    [SettingHotReloadable(true)]
    public string CloneHeroSongDirectory { get; set; } = "first_install";

    [SettingGroup("Clone Hero")]
    [SettingDisplay("Clone Hero Data Directory")]
    [SettingEditor(SettingEditorKind.DirectoryPicker)]
    [SettingHotReloadable(false)]
    [SettingRequiresRestart]
    public string CloneHeroDataDirectory { get; set; } = "first_install";

    [SettingGroup("ChartHub Server")]
    [SettingDisplay("Server API Bearer Token")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    public string ServerApiAuthToken { get; set; } = string.Empty;

    [SettingGroup("ChartHub Server")]
    [SettingDisplay("Server Base URL")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    public string ServerApiBaseUrl { get; set; } = string.Empty;

    [SettingGroup("Transfers")]
    [SettingDisplay("Transfer Concurrency Cap")]
    [SettingEditor(SettingEditorKind.Number)]
    [SettingHotReloadable(true)]
    public int TransferOrchestratorConcurrencyCap { get; set; } = 2;

    [SettingGroup("UI")]
    [SettingDisplay("Expand Install Log By Default")]
    [SettingEditor(SettingEditorKind.Toggle)]
    [SettingHotReloadable(true)]
    public bool InstallLogExpanded { get; set; } = true;
}
