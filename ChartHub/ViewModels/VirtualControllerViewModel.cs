using System.ComponentModel;
using System.Runtime.CompilerServices;

using ChartHub.Localization;
using ChartHub.Services;
using ChartHub.Strings;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

namespace ChartHub.ViewModels;

public sealed class VirtualControllerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppGlobalSettings? _globalSettings;
    private readonly IInputWebSocketService _webSocketService;
    private readonly IOrientationService _orientationService;
    private readonly IDeviceDisplayNameProvider _deviceDisplayNameProvider;
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
    public IRelayCommand<string?> PressButtonCommand { get; }
    public IRelayCommand<string?> ReleaseButtonCommand { get; }
    public IRelayCommand<string?> SetDPadCommand { get; }

    public VirtualControllerViewModel() : this(null, new InputWebSocketService(), new NullOrientationService(), new DeviceDisplayNameProvider()) { }

    public VirtualControllerViewModel(
        AppGlobalSettings? globalSettings,
        IInputWebSocketService webSocketService,
        IOrientationService orientationService,
        IDeviceDisplayNameProvider deviceDisplayNameProvider)
    {
        _globalSettings = globalSettings;
        _webSocketService = webSocketService;
        _orientationService = orientationService;
        _deviceDisplayNameProvider = deviceDisplayNameProvider;
        _statusMessage = UiLocalization.Get("Input.Controller.Status.Disconnected");

        ActivateCommand = new RelayCommand(Activate);
        DeactivateCommand = new RelayCommand(Deactivate);
        PressButtonCommand = new RelayCommand<string?>(buttonId => ObserveBackgroundTask(SendButtonAsync(buttonId, true), "Controller press"));
        ReleaseButtonCommand = new RelayCommand<string?>(buttonId => ObserveBackgroundTask(SendButtonAsync(buttonId, false), "Controller release"));
        SetDPadCommand = new RelayCommand<string?>(encoded => ObserveBackgroundTask(SendDPadAsync(encoded), "Controller dpad"));
    }

    public void Activate()
    {
        _orientationService.RequestLandscape();
        ObserveBackgroundTask(ConnectAsync(), "Controller WS connect");
    }

    public void Deactivate()
    {
        _orientationService.RestoreDefault();
        ObserveBackgroundTask(_webSocketService.DisconnectAsync(), "Controller WS disconnect");
        IsConnected = false;
        StatusMessage = UiLocalization.Get("Input.Controller.Status.Disconnected");
    }

    private async Task ConnectAsync()
    {
        string baseUrl = _globalSettings?.ServerApiBaseUrl ?? string.Empty;
        string token = _globalSettings?.ServerApiAuthToken ?? string.Empty;

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            StatusMessage = UiLocalization.Get("Input.Controller.Status.Disconnected");
            return;
        }

        try
        {
            string deviceName = _deviceDisplayNameProvider.GetDisplayName();
            await _webSocketService.ConnectAsync(baseUrl, token, "api/v1/input/controller/ws", deviceName).ConfigureAwait(false);
            IsConnected = true;
            StatusMessage = UiLocalization.Get("Input.Controller.Status.Connected");
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusMessage = UiLocalization.Get("Input.Controller.Status.Disconnected");
            Logger.LogError("VirtualControllerViewModel", "WebSocket connect failed", ex);
        }
    }

    private async Task SendButtonAsync(string? buttonId, bool pressed)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(buttonId))
        {
            return;
        }

        await _webSocketService.SendAsync(new
        {
            type = "btn",
            buttonId,
            pressed,
        }).ConfigureAwait(false);
    }

    private async Task SendDPadAsync(string? encoded)
    {
        // Encoded as "x,y" e.g. "1,0" or "-1,0"
        if (!IsConnected || string.IsNullOrWhiteSpace(encoded))
        {
            return;
        }

        string[] parts = encoded.Split(',');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out int x)
            || !int.TryParse(parts[1], out int y))
        {
            return;
        }

        await _webSocketService.SendAsync(new
        {
            type = "dpad",
            x,
            y,
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
            t => Logger.LogError("VirtualControllerViewModel", $"Background task failed: {context}", t.Exception!.GetBaseException()),
            default,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
