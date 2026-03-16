namespace RhythmVerseClient.Configuration.Models;

public sealed class AppConfigRoot
{
    public const int CurrentVersion = 2;

    public int ConfigVersion { get; set; } = CurrentVersion;

    public RuntimeAppConfig Runtime { get; set; } = new();

    public GoogleAuthConfig GoogleAuth { get; set; } = new();
}
