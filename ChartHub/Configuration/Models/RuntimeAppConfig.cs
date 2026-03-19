using ChartHub.Configuration.Metadata;

namespace ChartHub.Configuration.Models;

public sealed class RuntimeAppConfig
{
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

    [SettingGroup("Sync API")]
    [SettingDisplay("Loopback Sync API Token")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    public string SyncApiAuthToken { get; set; } = string.Empty;

    [SettingGroup("Sync API")]
    [SettingDisplay("Allow Event State Override")]
    [SettingEditor(SettingEditorKind.Toggle)]
    [SettingHotReloadable(true)]
    public bool AllowSyncApiStateOverride { get; set; }
}
