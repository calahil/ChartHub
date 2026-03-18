using ChartHub.Configuration.Metadata;

namespace ChartHub.Configuration.Models;

public sealed class GoogleAuthConfig
{
    [SettingGroup("Cloud Provider (Google)")]
    [SettingDisplay("Android OAuth Client ID")]
    [SettingDescription("Google-specific, non-secret OAuth client identifier for Android auth flow.")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    public string? AndroidClientId { get; set; }

    [SettingGroup("Cloud Provider (Google)")]
    [SettingDisplay("Desktop OAuth Client ID")]
    [SettingDescription("Google-specific, non-secret OAuth client identifier for desktop auth flow.")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    public string? DesktopClientId { get; set; }
}
