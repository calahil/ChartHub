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
    [SettingDisplay("Desktop Sync API URL")]
    [SettingDescription("Companion default target URL. This is the URL used by the client before pair-claim returns a resolved host.")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    [SettingPlatforms(SettingPlatformTargets.Android)]
    public string SyncApiDesktopBaseUrl { get; set; } = "http://127.0.0.1:15123";

    [SettingGroup("Sync API")]
    [SettingDisplay("Desktop Sync Listen Prefix")]
    [SettingDescription("Desktop host binding prefix. Use loopback for local-only mode, or wildcard/host prefixes when enabling LAN pairing.")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(false)]
    [SettingRequiresRestart]
    [SettingPlatforms(SettingPlatformTargets.Desktop)]
    public string SyncApiListenPrefix { get; set; } = "http://127.0.0.1:15123/";

    [SettingGroup("Sync API")]
    [SettingDisplay("Desktop Sync Advertised URL Override (Advanced)")]
    [SettingDescription("Optional override for the URL returned to companions during pairing and QR bootstrap. Leave blank to auto-resolve from listen settings.")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(false)]
    [SettingRequiresRestart]
    [SettingPlatforms(SettingPlatformTargets.Desktop)]
    public string SyncApiAdvertisedBaseUrl { get; set; } = string.Empty;

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
    public int SyncApiMaxRequestBodyBytes { get; set; } = 64 * 1024;

    [SettingGroup("Sync API")]
    [SettingDisplay("Request Body Timeout (ms)")]
    [SettingEditor(SettingEditorKind.Number)]
    [SettingHotReloadable(true)]
    [SettingPlatforms(SettingPlatformTargets.Desktop)]
    public int SyncApiBodyReadTimeoutMs { get; set; } = 1000;

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
