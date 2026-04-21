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

    private readonly ServerStatusService _statusService;
    private readonly CancellationTokenSource _cts = new();
    private bool _isPresent;
    private bool _uinputAvailable = true;
    private string _deviceName = string.Empty;
    private string _userEmail = string.Empty;

    public HudViewModel(ServerStatusService statusService)
    {
        _statusService = statusService;
        Version = ReadVersion();
        _ = RunWatchLoopAsync(_cts.Token);
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
