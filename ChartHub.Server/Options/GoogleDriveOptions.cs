namespace ChartHub.Server.Options;

public sealed class GoogleDriveOptions
{
    public const string SectionName = "GoogleDrive";

    public string ApiKey { get; set; } = string.Empty;
}
