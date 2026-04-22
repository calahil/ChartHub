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
    [SettingDeveloperOnly]
    public bool UseMockData { get; set; }

    [SettingHidden]
    public string ServerApiAuthToken { get; set; } = string.Empty;

    [SettingHidden]
    public string ServerApiBaseUrl { get; set; } = string.Empty;

    [SettingHidden]
    public bool InstallLogExpanded { get; set; } = true;

    [SettingGroup("General")]
    [SettingDisplay("UI Language")]
    [SettingDescription("Culture name used for localized UI text (example: en-US).")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(false)]
    [SettingRequiresRestart]
    public string UiCulture { get; set; } = "en-US";

    [SettingGroup("Input & Remote")]
    [SettingDisplay("Android Volume Buttons Control Server Volume")]
    [SettingEditor(SettingEditorKind.Toggle)]
    [SettingHotReloadable(true)]
    [SettingPlatforms(SettingPlatformTargets.Android)]
    public bool AndroidVolumeButtonsControlServerVolume { get; set; }

    [SettingGroup("Input & Remote")]
    [SettingDisplay("Mouse Speed Multiplier")]
    [SettingDescription("Scales touchpad pointer deltas before sending to the server. Increase if the cursor moves too slowly.")]
    [SettingEditor(SettingEditorKind.Number)]
    [SettingHotReloadable(true)]
    public double MouseSpeedMultiplier { get; set; } = 4.0;

    [SettingGroup("Input & Remote")]
    [SettingDisplay("Device Display Name Override")]
    [SettingDescription("Optional custom device name shown to ChartHub Server and the HUD. Leave blank to use the platform device name.")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    public string DeviceDisplayNameOverride { get; set; } = string.Empty;

    [SettingHidden]
    public int LastSelectedMainTabIndex { get; set; }
}
