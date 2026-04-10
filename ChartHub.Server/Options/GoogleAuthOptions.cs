namespace ChartHub.Server.Options;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    public string[] AllowedAudiences { get; set; } = [];
}
