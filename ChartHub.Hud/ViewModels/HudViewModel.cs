using System.ComponentModel;
using System.Runtime.CompilerServices;

using Avalonia.Media;

using ChartHub.Hud.Services;

namespace ChartHub.Hud.ViewModels;

public sealed class HudViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly IBrush ConnectedBrush = Brushes.LimeGreen;
    private static readonly IBrush DisconnectedBrush = Brushes.Red;

    private readonly ServerStatusService _statusService;
    private readonly CancellationTokenSource _cts = new();
    private int _deviceCount;

    public HudViewModel(ServerStatusService statusService)
    {
        _statusService = statusService;
        _ = RunWatchLoopAsync(_cts.Token);
    }

    public int DeviceCount
    {
        get => _deviceCount;
        private set
        {
            if (_deviceCount == value)
            {
                return;
            }

            _deviceCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IndicatorColor));
            OnPropertyChanged(nameof(DeviceCountLabel));
        }
    }

    public IBrush IndicatorColor => _deviceCount > 0 ? ConnectedBrush : DisconnectedBrush;

    public string DeviceCountLabel => _deviceCount == 1
        ? "1 device"
        : $"{_deviceCount} devices";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task RunWatchLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (int count in _statusService.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            DeviceCount = count;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
