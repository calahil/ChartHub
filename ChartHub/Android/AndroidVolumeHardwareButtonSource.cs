#if ANDROID
using Android.Views;

using ChartHub.Utilities;

namespace ChartHub.Services;

public sealed class AndroidVolumeHardwareButtonSource(AppGlobalSettings globalSettings) : IVolumeHardwareButtonSource
{
    private readonly AppGlobalSettings _globalSettings = globalSettings;

    public event EventHandler<int>? VolumeStepRequested;

    public bool TryHandlePlatformKey(int platformKeyCode)
    {
        if (!_globalSettings.AndroidVolumeButtonsControlServerVolume)
        {
            return false;
        }

        if (platformKeyCode == (int)Keycode.VolumeUp)
        {
            VolumeStepRequested?.Invoke(this, 5);
            return true;
        }

        if (platformKeyCode == (int)Keycode.VolumeDown)
        {
            VolumeStepRequested?.Invoke(this, -5);
            return true;
        }

        return false;
    }
}
#endif