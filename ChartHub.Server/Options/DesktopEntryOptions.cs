namespace ChartHub.Server.Options;

public sealed class DesktopEntryOptions
{
    public const string SectionName = "DesktopEntry";

    public bool Enabled { get; set; } = true;

    public string CatalogDirectory { get; set; } = "/usr/share/applications";

    public string IconCacheDirectory { get; set; } = "cache/desktop-entry-icons";

    public int SseIntervalSeconds { get; set; } = 2;
}
