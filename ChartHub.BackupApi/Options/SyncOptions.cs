namespace ChartHub.BackupApi.Options;

public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 10080;

    public int RecordsPerPage { get; set; } = 100;

    public int MaxPagesPerRun { get; set; } = 5000;

    public int CursorMaxAgeMinutes { get; set; } = 120;

    public int PageRewindOnResume { get; set; } = 2;

    /// <summary>
    /// Minutes to wait after startup before the first sync cycle runs.
    /// Set via the environment variable <c>Sync__InitialDelayMinutes</c> (e.g. 1440 for 24 hours).
    /// Defaults to 0 (sync immediately on startup).
    /// </summary>
    public int InitialDelayMinutes { get; set; } = 0;
}
