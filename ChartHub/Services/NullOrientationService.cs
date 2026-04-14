namespace ChartHub.Services;

/// <summary>No-op orientation service used on desktop platforms.</summary>
public sealed class NullOrientationService : IOrientationService
{
    public void RequestLandscape() { }

    public void RestoreDefault() { }
}
