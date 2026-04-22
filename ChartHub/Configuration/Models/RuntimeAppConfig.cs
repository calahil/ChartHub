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

    [SettingGroup("UI")]
    [SettingDisplay("Expand Install Log By Default")]
    [SettingEditor(SettingEditorKind.Toggle)]
    [SettingHotReloadable(true)]
    public bool InstallLogExpanded { get; set; } = true;

    [SettingGroup("UI")]
    [SettingDisplay("UI Culture")]
    [SettingDescription("Culture name used for localized UI text (example: en-US).")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(false)]
    [SettingRequiresRestart]
    public string UiCulture { get; set; } = "en-US";

    [SettingGroup("ChartHub Server")]
    [SettingDisplay("Android Volume Buttons Control Server Volume")]
    [SettingEditor(SettingEditorKind.Toggle)]
    [SettingHotReloadable(true)]
    public bool AndroidVolumeButtonsControlServerVolume { get; set; }

    [SettingGroup("Input")]
    [SettingDisplay("Mouse Speed Multiplier")]
    [SettingDescription("Scales touchpad pointer deltas before sending to the server. Increase if the cursor moves too slowly.")]
    [SettingEditor(SettingEditorKind.Number)]
    [SettingHotReloadable(true)]
    public double MouseSpeedMultiplier { get; set; } = 4.0;

    [SettingHidden]
    public int LastSelectedMainTabIndex { get; set; }
}
