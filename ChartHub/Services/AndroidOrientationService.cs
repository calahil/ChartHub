#if ANDROID
using Android.Content.PM;

namespace ChartHub.Services;

/// <summary>
/// Locks/unlocks screen orientation by modifying the current Android activity's
/// RequestedOrientation. All code is guarded with #if ANDROID.
/// </summary>
public sealed class AndroidOrientationService : IOrientationService
{
    public void RequestLandscape()
    {
        if (ChartHub.MainActivity.Current is { } activity)
        {
            activity.RunOnUiThread(() =>
            {
                activity.RequestedOrientation = ScreenOrientation.SensorLandscape;
            });
        }
    }

    public void RestoreDefault()
    {
        if (ChartHub.MainActivity.Current is { } activity)
        {
            activity.RunOnUiThread(() =>
            {
                activity.RequestedOrientation = ScreenOrientation.Unspecified;
            });
        }
    }
}
#endif
