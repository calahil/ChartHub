namespace ChartHub.Server.Options;

public sealed class HudOptions
{
    public const string SectionName = "Hud";

    /// <summary>
    /// Absolute path to the ChartHub.Hud executable.
    /// When empty, HUD lifecycle management is disabled.
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Port ChartHub.Server is listening on, passed to the HUD subprocess as --server-port.
    /// Defaults to 5000 if not configured. Override in appsettings to match ASPNETCORE_URLS.
    /// </summary>
    public int ServerPort { get; set; } = 5000;
}
