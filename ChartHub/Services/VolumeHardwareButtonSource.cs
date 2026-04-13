namespace ChartHub.Services;

public interface IVolumeHardwareButtonSource
{
    event EventHandler<int>? VolumeStepRequested;

    bool TryHandlePlatformKey(int platformKeyCode);
}

public sealed class NoOpVolumeHardwareButtonSource : IVolumeHardwareButtonSource
{
    public event EventHandler<int>? VolumeStepRequested
    {
        add { }
        remove { }
    }

    public bool TryHandlePlatformKey(int platformKeyCode)
    {
        return false;
    }
}