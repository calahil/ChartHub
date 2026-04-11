namespace ChartHub.Server.Options;

public sealed class ServerLoggingOptions
{
    public const string SectionName = "ServerLogging";

    public string LogDirectory { get; init; } = "logs";

    public string FileName { get; init; } = "charthub-server.log";
}