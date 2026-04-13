#if ANDROID
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

using Avalonia;
using Avalonia.Android;

using ChartHub.Services;

using Microsoft.Extensions.DependencyInjection;

namespace ChartHub;

[Activity(
    Label = "ChartHub",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Keep app content inside system bars instead of edge-to-edge drawing.
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && !OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            Window?.SetDecorFitsSystemWindows(true);
        }
    }

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (App.ServiceProvider?.GetService<IVolumeHardwareButtonSource>() is { } source
            && source.TryHandlePlatformKey((int)keyCode))
        {
            return true;
        }

        return base.OnKeyDown(keyCode, e);
    }
}
#endif
