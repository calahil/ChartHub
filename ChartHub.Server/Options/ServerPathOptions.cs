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
}
