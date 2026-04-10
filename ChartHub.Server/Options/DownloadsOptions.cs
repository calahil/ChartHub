namespace ChartHub.Server.Options;

public sealed class DownloadsOptions
{
    public const string SectionName = "Downloads";

    public int CompletedJobRetentionDays { get; set; } = 7;
}
