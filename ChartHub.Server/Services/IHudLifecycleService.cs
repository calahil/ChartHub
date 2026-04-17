namespace ChartHub.Server.Services;

public interface IHudLifecycleService
{
    /// <summary>Kills the HUD process to reclaim its memory before launching a game.</summary>
    Task SuspendAsync(CancellationToken cancellationToken);

    /// <summary>Re-spawns the HUD process, e.g. after a game exits.</summary>
    Task ResumeAsync(CancellationToken cancellationToken);
}
