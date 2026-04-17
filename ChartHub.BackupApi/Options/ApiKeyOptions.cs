namespace ChartHub.BackupApi.Options;

public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKey";

    public string Key { get; set; } = string.Empty;
}
