namespace ChartHub.BackupApi.Options;

public sealed class DownloadOptions
{
    public const string SectionName = "Downloads";

    public string Mode { get; set; } = "redirect";
}
