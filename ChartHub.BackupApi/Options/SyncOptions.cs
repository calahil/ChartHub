namespace ChartHub.BackupApi.Options;

public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 60;

    public int RecordsPerPage { get; set; } = 100;

    public int MaxPagesPerRun { get; set; } = 2000;
}
