using RhythmVerseClient.Configuration.Metadata;

namespace RhythmVerseClient.Configuration.Models;

public sealed class GoogleAuthConfig
{
    [SettingGroup("Google")]
    [SettingDisplay("Android Client ID")]
    [SettingDescription("Non-secret OAuth client identifier for Android auth flow.")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    public string? AndroidClientId { get; set; }

    [SettingGroup("Google")]
    [SettingDisplay("Desktop Client ID")]
    [SettingDescription("Non-secret OAuth client identifier for desktop auth flow.")]
    [SettingEditor(SettingEditorKind.Text)]
    [SettingHotReloadable(true)]
    public string? DesktopClientId { get; set; }
}
