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

    [SettingGroup("Sync API")]
    [SettingDisplay("Loopback Sync API Token")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    public string SyncApiAuthToken { get; set; } = string.Empty;

    [SettingGroup("Sync API")]
    [SettingDisplay("Companion Device Label")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    public string SyncApiDeviceLabel { get; set; } = "Android Companion";

    [SettingGroup("Sync API")]
    [SettingDisplay("Pair Code")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    public string SyncApiPairCode { get; set; } = string.Empty;

    [SettingGroup("Sync API")]
    [SettingDisplay("Pair Code Issued At (UTC)")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    [SettingHidden]
    public string SyncApiPairCodeIssuedAtUtc { get; set; } = string.Empty;

    [SettingGroup("Sync API")]
    [SettingDisplay("Pair Code TTL (minutes)")]
    [SettingEditor(SettingEditorKind.Number)]
    [SettingHotReloadable(true)]
    public int SyncApiPairCodeTtlMinutes { get; set; } = 10;

    [SettingHidden]
    public string SyncApiLastPairedDeviceLabel { get; set; } = string.Empty;

    [SettingHidden]
    public string SyncApiLastPairedAtUtc { get; set; } = string.Empty;

    [SettingHidden]
    public string SyncApiPairingHistoryJson { get; set; } = "[]";

    [SettingGroup("Sync API")]
    [SettingDisplay("Saved Connections JSON")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    [SettingHidden]
    public string SyncApiSavedConnectionsJson { get; set; } = "[]";

    [SettingGroup("Sync API")]
    [SettingDisplay("Preferred Companion Endpoint")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    [SettingHidden]
    public string SyncApiPreferredBaseUrl { get; set; } = string.Empty;

    [SettingGroup("Sync API")]
    [SettingDisplay("Allow Event State Override")]
    [SettingEditor(SettingEditorKind.Toggle)]
    [SettingHotReloadable(true)]
    [SettingPlatforms(SettingPlatformTargets.Desktop)]
    public bool AllowSyncApiStateOverride { get; set; }

    [SettingGroup("Sync API")]
    [SettingDisplay("Max Request Body Bytes")]
    [SettingEditor(SettingEditorKind.Number)]
    [SettingHotReloadable(true)]
    [SettingPlatforms(SettingPlatformTargets.Desktop)]
    public int SyncApiMaxRequestBodyBytes { get; set; } = 32 * 1024 * 1024;

    [SettingGroup("Sync API")]
    [SettingDisplay("Request Body Timeout (ms)")]
    [SettingEditor(SettingEditorKind.Number)]
    [SettingHotReloadable(true)]
    [SettingPlatforms(SettingPlatformTargets.Desktop)]
    public int SyncApiBodyReadTimeoutMs { get; set; } = 30_000;

    [SettingGroup("Sync API")]
    [SettingDisplay("Mutation Wait Timeout (ms)")]
    [SettingEditor(SettingEditorKind.Number)]
    [SettingHotReloadable(true)]
    [SettingPlatforms(SettingPlatformTargets.Desktop)]
    public int SyncApiMutationWaitTimeoutMs { get; set; } = 250;

    [SettingGroup("Sync API")]
    [SettingDisplay("Slow Request Threshold (ms)")]
    [SettingEditor(SettingEditorKind.Number)]
    [SettingHotReloadable(true)]
    [SettingPlatforms(SettingPlatformTargets.Desktop)]
    public int SyncApiSlowRequestThresholdMs { get; set; } = 500;

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
