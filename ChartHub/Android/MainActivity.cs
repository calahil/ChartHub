#if ANDROID
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

using Avalonia;
using Avalonia.Android;

using ChartHub.Services;
using ChartHub.Utilities;

using Microsoft.Extensions.DependencyInjection;

namespace ChartHub;

[Activity(
    Label = "ChartHub",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>
    /// The currently resumed <see cref="MainActivity"/> instance.
    /// Set on <see cref="OnResume"/> and cleared on <see cref="OnDestroy"/>.
    /// Used by services that need to interact with the Android UI (e.g. orientation lock).
    /// </summary>
    public static MainActivity? Current { get; private set; }

    private IAuthSessionService? _authSessionService;
    private EventHandler? _authSessionStateChangedHandler;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Keep app content inside system bars instead of edge-to-edge drawing.
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && !OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            Window?.SetDecorFitsSystemWindows(true);
        }

        _authSessionService = App.ServiceProvider?.GetService<IAuthSessionService>();
        if (_authSessionService is not null)
        {
            _authSessionStateChangedHandler = (_, _) => EnsurePresenceConnectionForCurrentAuthState();
            _authSessionService.SessionStateChanged += _authSessionStateChangedHandler;
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        Current = this;

        EnsurePresenceConnectionForCurrentAuthState();
    }

    protected override void OnPause()
    {
        base.OnPause();
        IPresenceWebSocketService? svc = App.ServiceProvider?.GetService<IPresenceWebSocketService>();
        if (svc != null)
        {
            _ = svc.DisconnectAsync();
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (_authSessionService is not null && _authSessionStateChangedHandler is not null)
        {
            _authSessionService.SessionStateChanged -= _authSessionStateChangedHandler;
            _authSessionStateChangedHandler = null;
        }

        if (Current == this)
        {
            Current = null;
        }
    }

    private void EnsurePresenceConnectionForCurrentAuthState()
    {
        IPresenceWebSocketService? svc = App.ServiceProvider?.GetService<IPresenceWebSocketService>();
        AppGlobalSettings? settings = App.ServiceProvider?.GetService<AppGlobalSettings>();
        IDeviceDisplayNameProvider? deviceNameProvider = App.ServiceProvider?.GetService<IDeviceDisplayNameProvider>();
        IAuthSessionService? authSessionService = _authSessionService ?? App.ServiceProvider?.GetService<IAuthSessionService>();

        if (svc is null || settings is null)
        {
            return;
        }

        if (authSessionService?.CurrentState == AuthSessionState.Authenticated
            && !string.IsNullOrWhiteSpace(settings.ServerApiBaseUrl)
            && !string.IsNullOrWhiteSpace(authSessionService.CurrentAccessToken))
        {
            string deviceName = deviceNameProvider?.GetDisplayName() ?? "unknown-device";
            _ = svc.ConnectAsync(settings.ServerApiBaseUrl, authSessionService.CurrentAccessToken!, deviceName);
            return;
        }

        _ = svc.DisconnectAsync();
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
