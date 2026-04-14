using System.ComponentModel;
using System.Runtime.CompilerServices;

using ChartHub.Localization;
using ChartHub.Services;
using ChartHub.Strings;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

namespace ChartHub.ViewModels;

public sealed class VirtualKeyboardViewModel : INotifyPropertyChanged, IDisposable
{
    // Linux key codes for special keys (subset of UinputNative constants).
    private const int LinuxKeyEnter = 28;
    private const int LinuxKeyBackspace = 14;
    private const int LinuxKeyEsc = 1;
    private const int LinuxKeyTab = 15;

    private readonly AppGlobalSettings? _globalSettings;
    private readonly IInputWebSocketService _webSocketService;
    private bool _isConnected;
    private string _statusMessage = string.Empty;
    private string _inputBuffer = string.Empty;
    private string _previousBuffer = string.Empty;
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

    /// <summary>
    /// Bound to the hidden capture TextBox. The setter diffs against the previous value
    /// to extract new characters and ship them to the server.
    /// </summary>
    public string InputBuffer
    {
        get => _inputBuffer;
        set
        {
            string previous = _previousBuffer;
            _previousBuffer = value;
            _inputBuffer = value;
            OnPropertyChanged();
            OnTextChanged(previous, value);
        }
    }

    public IRelayCommand ActivateCommand { get; }
    public IRelayCommand DeactivateCommand { get; }
    public IRelayCommand SendEnterCommand { get; }
    public IRelayCommand SendBackspaceCommand { get; }
    public IRelayCommand SendEscapeCommand { get; }
    public IRelayCommand SendTabCommand { get; }

    public VirtualKeyboardViewModel() : this(null, new InputWebSocketService()) { }

    public VirtualKeyboardViewModel(
        AppGlobalSettings? globalSettings,
        IInputWebSocketService webSocketService)
    {
        _globalSettings = globalSettings;
        _webSocketService = webSocketService;
        _statusMessage = UiLocalization.Get("Input.Keyboard.Status.Disconnected");

        ActivateCommand = new RelayCommand(Activate);
        DeactivateCommand = new RelayCommand(Deactivate);
        SendEnterCommand = new RelayCommand(() => ObserveBackgroundTask(SendKeyAsync(LinuxKeyEnter), "Keyboard enter"));
        SendBackspaceCommand = new RelayCommand(() => ObserveBackgroundTask(SendKeyAsync(LinuxKeyBackspace), "Keyboard backspace"));
        SendEscapeCommand = new RelayCommand(() => ObserveBackgroundTask(SendKeyAsync(LinuxKeyEsc), "Keyboard escape"));
        SendTabCommand = new RelayCommand(() => ObserveBackgroundTask(SendKeyAsync(LinuxKeyTab), "Keyboard tab"));
    }

    public void Activate()
    {
        // No orientation lock for keyboard — portrait is natural typing posture.
        ObserveBackgroundTask(ConnectAsync(), "Keyboard WS connect");
    }

    public void Deactivate()
    {
        ObserveBackgroundTask(_webSocketService.DisconnectAsync(), "Keyboard WS disconnect");
        IsConnected = false;
        StatusMessage = UiLocalization.Get("Input.Keyboard.Status.Disconnected");
    }

    private void OnTextChanged(string previous, string current)
    {
        if (!IsConnected)
        {
            return;
        }

        // The hidden TextBox may have characters appended or deleted.
        // We only forward the incremental new characters at the end.
        // Backspace key is handled via SendBackspaceCommand separately,
        // so here we only fire for appended characters.
        if (current.Length > previous.Length)
        {
            string added = current[previous.Length..];
            foreach (char c in added)
            {
                ObserveBackgroundTask(SendCharAsync(c), "Keyboard char");
            }
        }
    }

    private async Task ConnectAsync()
    {
        string baseUrl = _globalSettings?.ServerApiBaseUrl ?? string.Empty;
        string token = _globalSettings?.ServerApiAuthToken ?? string.Empty;

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            StatusMessage = UiLocalization.Get("Input.Keyboard.Status.Disconnected");
            return;
        }

        try
        {
            await _webSocketService.ConnectAsync(baseUrl, token, "api/v1/input/keyboard/ws").ConfigureAwait(false);
            IsConnected = true;
            StatusMessage = UiLocalization.Get("Input.Keyboard.Status.Connected");
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusMessage = UiLocalization.Get("Input.Keyboard.Status.Disconnected");
            Logger.LogError("VirtualKeyboardViewModel", "WebSocket connect failed", ex);
        }
    }

    private async Task SendCharAsync(char c)
    {
        await _webSocketService.SendAsync(new
        {
            type = "char",
            @char = c.ToString(),
        }).ConfigureAwait(false);
    }

    private async Task SendKeyAsync(int linuxKeyCode)
    {
        if (!IsConnected)
        {
            return;
        }

        // Press + release cycle
        await _webSocketService.SendAsync(new { type = "key", linuxKeyCode, pressed = true }).ConfigureAwait(false);
        await _webSocketService.SendAsync(new { type = "key", linuxKeyCode, pressed = false }).ConfigureAwait(false);
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
            t => Logger.LogError("VirtualKeyboardViewModel", $"Background task failed: {context}", t.Exception!.GetBaseException()),
            default,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
