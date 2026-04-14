using System.ComponentModel;
using System.Runtime.CompilerServices;

using ChartHub.Localization;
using ChartHub.Services;
using ChartHub.Strings;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

namespace ChartHub.ViewModels;

public sealed class VirtualTouchPadViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppGlobalSettings? _globalSettings;
    private readonly IInputWebSocketService _webSocketService;
    private readonly IOrientationService _orientationService;
    private bool _isConnected;
    private string _statusMessage = string.Empty;
    private bool _disposed;

    public InputPageStrings PageStrings { get; } = new();

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected == value)
            {
                return;
            }

            _isConnected = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public IRelayCommand ActivateCommand { get; }
    public IRelayCommand DeactivateCommand { get; }
    public IRelayCommand<string?> PressMouseButtonCommand { get; }
    public IRelayCommand<string?> ReleaseMouseButtonCommand { get; }

    public VirtualTouchPadViewModel() : this(null, new InputWebSocketService(), new NullOrientationService()) { }

    public VirtualTouchPadViewModel(
        AppGlobalSettings? globalSettings,
        IInputWebSocketService webSocketService,
        IOrientationService orientationService)
    {
        _globalSettings = globalSettings;
        _webSocketService = webSocketService;
        _orientationService = orientationService;
        _statusMessage = UiLocalization.Get("Input.Touchpad.Status.Disconnected");

        ActivateCommand = new RelayCommand(Activate);
        DeactivateCommand = new RelayCommand(Deactivate);
        PressMouseButtonCommand = new RelayCommand<string?>(side => ObserveBackgroundTask(SendMouseButtonAsync(side, true), "Touchpad press"));
        ReleaseMouseButtonCommand = new RelayCommand<string?>(side => ObserveBackgroundTask(SendMouseButtonAsync(side, false), "Touchpad release"));
    }

    public void Activate()
    {
        _orientationService.RequestLandscape();
        ObserveBackgroundTask(ConnectAsync(), "Touchpad WS connect");
    }

    public void Deactivate()
    {
        _orientationService.RestoreDefault();
        ObserveBackgroundTask(_webSocketService.DisconnectAsync(), "Touchpad WS disconnect");
        IsConnected = false;
        StatusMessage = UiLocalization.Get("Input.Touchpad.Status.Disconnected");
    }

    /// <summary>
    /// Called by the View when a pointer drag delta is captured.
    /// dx/dy are pixel deltas relative to the last pointer position.
    /// </summary>
    public void OnTouchPadDelta(int dx, int dy)
    {
        if (!IsConnected)
        {
            return;
        }

        ObserveBackgroundTask(SendMoveAsync(dx, dy), "Touchpad move");
    }

    private async Task ConnectAsync()
    {
        string baseUrl = _globalSettings?.ServerApiBaseUrl ?? string.Empty;
        string token = _globalSettings?.ServerApiAuthToken ?? string.Empty;

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            StatusMessage = UiLocalization.Get("Input.Touchpad.Status.Disconnected");
            return;
        }

        try
        {
            await _webSocketService.ConnectAsync(baseUrl, token, "api/v1/input/touchpad/ws").ConfigureAwait(false);
            IsConnected = true;
            StatusMessage = UiLocalization.Get("Input.Touchpad.Status.Connected");
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusMessage = UiLocalization.Get("Input.Touchpad.Status.Disconnected");
            Logger.LogError("VirtualTouchPadViewModel", "WebSocket connect failed", ex);
        }
    }

    private async Task SendMoveAsync(int dx, int dy)
    {
        await _webSocketService.SendAsync(new
        {
            type = "move",
            dx,
            dy,
        }).ConfigureAwait(false);
    }

    private async Task SendMouseButtonAsync(string? side, bool pressed)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(side))
        {
            return;
        }

        await _webSocketService.SendAsync(new
        {
            type = "mousebtn",
            side,
            pressed,
        }).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _webSocketService.Dispose();
    }

    private static void ObserveBackgroundTask(Task task, string context)
    {
        task.ContinueWith(
            t => Logger.LogError("VirtualTouchPadViewModel", $"Background task failed: {context}", t.Exception!.GetBaseException()),
            default,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
