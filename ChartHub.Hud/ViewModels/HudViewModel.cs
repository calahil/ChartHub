using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

using Avalonia.Media;

using ChartHub.Hud.Services;

namespace ChartHub.Hud.ViewModels;

public sealed class HudViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly IBrush ConnectedBrush = Brushes.LimeGreen;
    private static readonly IBrush UinputFaultBrush = Brushes.Orange;
    private static readonly IBrush DisconnectedBrush = Brushes.Red;
    private static readonly IBrush VolumeAccentBrush = new SolidColorBrush(Color.Parse("#FF7BA7E0"));
    private static readonly IBrush VolumeMutedBrush = new SolidColorBrush(Color.Parse("#FFB8844A"));
    private static readonly IBrush VolumeTrackBrush = new SolidColorBrush(Color.Parse("#FF2A2A2A"));
    private static readonly IBrush VolumeDisabledBrush = new SolidColorBrush(Color.Parse("#FF5A5A5A"));

    private readonly ServerStatusService _statusService;
    private readonly ServerVolumeService _volumeService;
    private readonly CancellationTokenSource _cts = new();
    private bool _isPresent;
    private bool _uinputAvailable = true;
    private string _deviceName = string.Empty;
    private string _userEmail = string.Empty;
    private bool _isVolumeAvailable;
    private bool _isMuted;
    private int _volumePercent;
    private double _viewportWidth = 1280;
    private double _viewportHeight = 720;

    public HudViewModel(ServerStatusService statusService, ServerVolumeService volumeService)
    {
        _statusService = statusService;
        _volumeService = volumeService;
        Version = ReadVersion();
        _ = RunWatchLoopAsync(_cts.Token);
        _ = RunVolumeLoopAsync(_cts.Token);
    }

    public bool IsPresent
    {
        get => _isPresent;
        private set
        {
            if (_isPresent == value)
            {
                return;
            }

            _isPresent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IndicatorColor));
        }
    }

    public bool UinputAvailable
    {
        get => _uinputAvailable;
        private set
        {
            if (_uinputAvailable == value)
            {
                return;
            }

            _uinputAvailable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IndicatorColor));
        }
    }

    public string DeviceName
    {
        get => _deviceName;
        private set
        {
            if (_deviceName == value)
            {
                return;
            }

            _deviceName = value;
            OnPropertyChanged();
        }
    }

    public string UserEmail
    {
        get => _userEmail;
        private set
        {
            if (_userEmail == value)
            {
                return;
            }

            _userEmail = value;
            OnPropertyChanged();
        }
    }

    public string Version { get; }

    public bool IsVolumeAvailable
    {
        get => _isVolumeAvailable;
        private set
        {
            if (_isVolumeAvailable == value)
            {
                return;
            }

            _isVolumeAvailable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeWidgetOpacity));
            OnPropertyChanged(nameof(VolumeValueBrush));
            OnPropertyChanged(nameof(VolumeTrackDisplayBrush));
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set
        {
            if (_isMuted == value)
            {
                return;
            }

            _isMuted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeValueBrush));
        }
    }

    public int VolumePercent
    {
        get => _volumePercent;
        private set
        {
            int normalized = Math.Clamp(value, 0, 100);
            if (_volumePercent == normalized)
            {
                return;
            }

            _volumePercent = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeLabel));
            OnPropertyChanged(nameof(VolumeFillHeight));
        }
    }

    public string VolumeLabel => IsVolumeAvailable ? $"{VolumePercent}%" : "N/A";

    public double VolumeWidgetOpacity => IsVolumeAvailable ? 1.0 : 0.45;

    public IBrush VolumeTrackDisplayBrush => IsVolumeAvailable ? VolumeTrackBrush : VolumeDisabledBrush;

    public IBrush VolumeValueBrush => !IsVolumeAvailable ? VolumeDisabledBrush : (IsMuted ? VolumeMutedBrush : VolumeAccentBrush);

    public double VolumeBarHeight => Math.Max(1, _viewportHeight * 0.25);

    public double VolumeFillHeight => VolumeBarHeight * (VolumePercent / 100.0);

    public double LogoSize => Math.Max(1, Math.Min(_viewportWidth, _viewportHeight) / 3.0);

    /// <summary>
    /// Green = present + uinput OK, Orange = present + uinput fault, Red = not present.
    /// </summary>
    public IBrush IndicatorColor =>
        _isPresent
            ? (_uinputAvailable ? ConnectedBrush : UinputFaultBrush)
            : DisconnectedBrush;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task RunWatchLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (HudStatus status in _statusService.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            IsPresent = status.IsPresent;
            UinputAvailable = status.UinputAvailable;
            DeviceName = status.DeviceName ?? string.Empty;
            UserEmail = status.UserEmail ?? string.Empty;
        }
    }

    public void UpdateViewport(double width, double height)
    {
        double normalizedWidth = Math.Max(1, width);
        double normalizedHeight = Math.Max(1, height);

        if (Math.Abs(_viewportWidth - normalizedWidth) < 0.5 && Math.Abs(_viewportHeight - normalizedHeight) < 0.5)
        {
            return;
        }

        _viewportWidth = normalizedWidth;
        _viewportHeight = normalizedHeight;

        OnPropertyChanged(nameof(LogoSize));
        OnPropertyChanged(nameof(VolumeBarHeight));
        OnPropertyChanged(nameof(VolumeFillHeight));
    }

    private async Task RunVolumeLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (HudVolumeStatus status in _volumeService.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            IsVolumeAvailable = status.IsAvailable;
            IsMuted = status.IsMuted;
            VolumePercent = status.ValuePercent;
        }
    }

    private static string ReadVersion()
    {
        string? info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(info))
        {
            return string.Empty;
        }

        // InformationalVersion can be "1.0.0-dev.6+abc1234"; show "v1.0.0-dev.6 · abc1234"
        int plusIdx = info.IndexOf('+', StringComparison.Ordinal);
        if (plusIdx >= 0)
        {
            string semver = info[..plusIdx];
            string commit = info[(plusIdx + 1)..];
            // Trim to 7 chars if it looks like a full git SHA.
            if (commit.Length > 7 && commit.All(c => Uri.IsHexDigit(c)))
            {
                commit = commit[..7];
            }

            return $"v{semver} · {commit}";
        }

        return $"v{info}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
