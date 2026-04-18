namespace ChartHub.Server.Options;

public sealed class UnityLaunchOptions
{
    public const string SectionName = "UnityLaunch";

    public bool Enabled { get; set; } = true;

    public Dictionary<string, string> BootConfig { get; set; } = [];

    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
}
