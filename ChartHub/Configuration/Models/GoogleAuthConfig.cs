using ChartHub.Configuration.Metadata;

namespace ChartHub.Configuration.Models;

public sealed class GoogleAuthConfig
{
    [SettingGroup("Developer (Google OAuth)")]
    [SettingDisplay("Android OAuth Client ID")]
    [SettingDescription("Google-specific, non-secret OAuth client identifier for Android auth flow.")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    [SettingDeveloperOnly]
    [SettingPlatforms(SettingPlatformTargets.Android)]
    public string? AndroidClientId { get; set; }

    [SettingGroup("Developer (Google OAuth)")]
    [SettingDisplay("Desktop OAuth Client ID")]
    [SettingDescription("Google-specific, non-secret OAuth client identifier for desktop auth flow.")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    [SettingDeveloperOnly]
    [SettingPlatforms(SettingPlatformTargets.Desktop)]
    public string? DesktopClientId { get; set; }
}
