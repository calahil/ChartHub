namespace ChartHub.Server.Services;

/// <summary>
/// Detects Unity games adjacent to a launched executable, patches their
/// <c>boot.config</c> with performance-oriented settings, and supplies
/// environment variables to inject before the process starts.
/// </summary>
public interface IUnityLaunchOptimizer
{
    /// <summary>
    /// If <paramref name="executablePath"/> belongs to a Unity game:
    /// <list type="bullet">
    ///   <item>Patches the game's <c>boot.config</c> with configured keys (idempotent).</item>
    ///   <item>Returns environment variables to inject into the launched process.</item>
    /// </list>
    /// If it is not a Unity game, or optimization is disabled, returns an empty dictionary
    /// and makes no filesystem changes.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> OptimizeAsync(string executablePath, CancellationToken cancellationToken);
}
