namespace ChartHub.Server.Options;

public sealed class ServerPathOptions
{
    public const string SectionName = "ServerPaths";

    public string ConfigRoot { get; set; } = "/config";

    public string ChartHubRoot { get; set; } = "/charthub";

    public string DownloadsDir { get; set; } = "/charthub/downloads";

    public string StagingDir { get; set; } = "/charthub/staging";

    public string CloneHeroRoot { get; set; } = "/clonehero";

    public string SqliteDbPath { get; set; } = "/config/charthub-server.db";

    /// <summary>
    /// Directory where post-processing (AI drum transcription) song previews are written.
    /// Derived at runtime as <see cref="CloneHeroRoot"/> + "-postprocess".
    /// </summary>
    public string CloneHeroPostProcessRoot => CloneHeroRoot.TrimEnd('/') + "-postprocess";

    /// <summary>
    /// Directory where original song folders are archived before a post-processed version
    /// is promoted to <see cref="CloneHeroRoot"/>.
    /// Derived at runtime as <see cref="CloneHeroRoot"/> + "-archive".
    /// </summary>
    public string CloneHeroArchiveRoot => CloneHeroRoot.TrimEnd('/') + "-archive";

    /// <summary>
    /// Secret key used to sign HMAC-SHA256 audio download URLs issued to runners.
    /// Set via the <c>ServerPaths__RunnerAudioSigningKey</c> environment variable or config.
    /// </summary>
    public string RunnerAudioSigningKey { get; set; } = "change-me-runner-audio-key";
}
