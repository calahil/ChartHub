namespace ChartHub.Server.Options;

public sealed class RunnerOptions
{
    public const string SectionName = "Runner";

    /// <summary>
    /// Static API key that grants access to runner management endpoints
    /// (e.g. issuing registration tokens) without requiring a user JWT.
    /// Used by CI to register new runner machines.
    /// Must be at least 32 characters. Leave empty to disable API key access.
    /// </summary>
    public string ManagementApiKey { get; set; } = "";
}
