using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Avalonia.Threading;

using ChartHub.Localization;
using ChartHub.Services;
using ChartHub.Strings;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

namespace ChartHub.ViewModels;

public sealed class VolumeViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppGlobalSettings? _globalSettings;
    public VolumePageStrings PageStrings { get; } = new();
    private readonly IChartHubServerApiClient? _serverApiClient;
    private readonly IVolumeHardwareButtonSource? _hardwareButtonSource;
    private readonly Func<Action, Task> _uiInvoke;
    private readonly CancellationTokenSource _streamCts = new();
    private readonly SemaphoreSlim _masterAdjustLock = new(1, 1);
    private bool _streamStarted;
    private ObservableCollection<VolumeSessionCardItem> _sessions = [];
    private int _currentMasterVolume;
    private int _pendingMasterVolume;
    private bool _masterMuted;
    private bool _isBusy;
    private bool _supportsPerApplicationSessions;
    private string _sessionSupportMessage = UiLocalization.Get("Volume.PerApplicationUnsupported");
    private string _statusMessage = UiLocalization.Get("Volume.NotInitialized");

    public bool IsCompanionMode => OperatingSystem.IsAndroid();

    public bool IsDesktopMode => !OperatingSystem.IsAndroid();

    public bool IsAndroidHardwareBindingVisible => IsCompanionMode;

    public ObservableCollection<VolumeSessionCardItem> Sessions
    {
        get => _sessions;
        private set
        {
            _sessions = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSessions));
            OnPropertyChanged(nameof(ShowEmptySessionsMessage));
        }
    }

    public bool HasSessions => Sessions.Count > 0;

    public bool ShowPerApplicationUnsupportedMessage => !SupportsPerApplicationSessions;

    public bool ShowEmptySessionsMessage => SupportsPerApplicationSessions && !HasSessions;

    public int CurrentMasterVolume
    {
        get => _currentMasterVolume;
        private set
        {
            if (_currentMasterVolume == value)
            {
                return;
            }

            _currentMasterVolume = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMasterDirty));
        }
    }

    public int PendingMasterVolume
    {
        get => _pendingMasterVolume;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (_pendingMasterVolume == clamped)
            {
                return;
            }

            _pendingMasterVolume = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMasterDirty));
            OnPropertyChanged(nameof(PendingMasterLabel));
        }
    }

    public string PendingMasterLabel => PageStrings.FormatPendingPercent(PendingMasterVolume);

    public bool IsMasterDirty => PendingMasterVolume != CurrentMasterVolume;

    public bool MasterMuted
    {
        get => _masterMuted;
        private set
        {
            if (_masterMuted == value)
            {
                return;
            }

            _masterMuted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MasterStateLabel));
        }
    }

    public string MasterStateLabel => MasterMuted
        ? UiLocalization.Get("Common.Muted")
        : UiLocalization.Format("Common.CurrentPercent", CurrentMasterVolume);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public bool SupportsPerApplicationSessions
    {
        get => _supportsPerApplicationSessions;
        private set
        {
            if (_supportsPerApplicationSessions == value)
            {
                return;
            }

            _supportsPerApplicationSessions = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowPerApplicationUnsupportedMessage));
            OnPropertyChanged(nameof(ShowEmptySessionsMessage));
        }
    }

    public string SessionSupportMessage
    {
        get => _sessionSupportMessage;
        private set
        {
            if (_sessionSupportMessage == value)
            {
                return;
            }

            _sessionSupportMessage = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsAndroidHardwareBindingEnabled
    {
        get => _globalSettings?.AndroidVolumeButtonsControlServerVolume ?? false;
        set
        {
            if (_globalSettings is null || _globalSettings.AndroidVolumeButtonsControlServerVolume == value)
            {
                return;
            }

            _globalSettings.AndroidVolumeButtonsControlServerVolume = value;
            OnPropertyChanged();
        }
    }

    private readonly AsyncRelayCommand _refreshCommand;
    public IAsyncRelayCommand RefreshCommand => _refreshCommand;

    private readonly AsyncRelayCommand _applyMasterCommand;
    public IAsyncRelayCommand ApplyMasterCommand => _applyMasterCommand;

    private readonly AsyncRelayCommand<VolumeSessionCardItem?> _applySessionCommand;
    public IAsyncRelayCommand<VolumeSessionCardItem?> ApplySessionCommand => _applySessionCommand;

    public VolumeViewModel(
        AppGlobalSettings? globalSettings = null,
        IChartHubServerApiClient? serverApiClient = null,
        IVolumeHardwareButtonSource? hardwareButtonSource = null,
        Func<Action, Task>? uiInvoke = null)
    {
        _globalSettings = globalSettings;
        _serverApiClient = serverApiClient;
        _hardwareButtonSource = hardwareButtonSource;
        _uiInvoke = uiInvoke ?? (async action => await Dispatcher.UIThread.InvokeAsync(action));

        _refreshCommand = new AsyncRelayCommand(RefreshAsync);
        _applyMasterCommand = new AsyncRelayCommand(ApplyMasterAsync);
        _applySessionCommand = new AsyncRelayCommand<VolumeSessionCardItem?>(ApplySessionAsync);

        if (_hardwareButtonSource is not null)
        {
            _hardwareButtonSource.VolumeStepRequested += OnVolumeStepRequested;
        }

        if (_globalSettings is not null)
        {
            _globalSettings.PropertyChanged += OnGlobalSettingsPropertyChanged;
        }

        ObserveBackgroundTask(InitializeAsync(), "Volume startup sync");
    }

    private async Task InitializeAsync()
    {
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task RefreshAsync()
    {
        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            await _uiInvoke(() =>
            {
                Sessions = [];
                StatusMessage = UiLocalization.Get("Volume.ConfigureLoad");
            });
            return;
        }

        IsBusy = true;
        try
        {
            ChartHubServerVolumeStateResponse state = await _serverApiClient!
                .GetVolumeStateAsync(baseUrl, bearerToken)
                .ConfigureAwait(false);
            await ApplySnapshotAsync(state, UiLocalization.Get("Volume.StateRefreshed")).ConfigureAwait(false);
            EnsureStreamStarted();
        }
        catch (Exception ex)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = UiLocalization.Format("Volume.RefreshFailed", ex.Message);
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyMasterAsync()
    {
        await ApplyMasterVolumeCoreAsync(PendingMasterVolume, UiLocalization.Get("Volume.MasterUpdated")).ConfigureAwait(false);
    }

    public async Task AdjustMasterVolumeAsync(int delta)
    {
        await _masterAdjustLock.WaitAsync().ConfigureAwait(false);
        try
        {
            int target = Math.Clamp(PendingMasterVolume + delta, 0, 100);
            await ApplyMasterVolumeCoreAsync(
                    target,
                    delta >= 0 ? UiLocalization.Get("Volume.MasterIncreased") : UiLocalization.Get("Volume.MasterDecreased"))
                .ConfigureAwait(false);
        }
        finally
        {
            _masterAdjustLock.Release();
        }
    }

    private async Task ApplyMasterVolumeCoreAsync(int valuePercent, string successMessage)
    {
        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            await _uiInvoke(() =>
            {
                StatusMessage = UiLocalization.Get("Volume.ConfigureMasterChange");
            });
            return;
        }

        IsBusy = true;
        try
        {
            ChartHubServerVolumeActionResponse response = await _serverApiClient!
                .SetMasterVolumeAsync(baseUrl, bearerToken, valuePercent)
                .ConfigureAwait(false);

            await _uiInvoke(() =>
            {
                CurrentMasterVolume = response.ValuePercent;
                PendingMasterVolume = response.ValuePercent;
                MasterMuted = response.IsMuted;
                StatusMessage = successMessage;
            });
        }
        catch (Exception ex)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = UiLocalization.Format("Volume.MasterUpdateFailed", ex.Message);
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplySessionAsync(VolumeSessionCardItem? session)
    {
        if (session is null)
        {
            return;
        }

        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            await _uiInvoke(() =>
            {
                StatusMessage = UiLocalization.Get("Volume.ConfigureSessionChange");
            });
            return;
        }

        session.IsBusy = true;
        try
        {
            ChartHubServerVolumeActionResponse response = await _serverApiClient!
                .SetSessionVolumeAsync(baseUrl, bearerToken, session.SessionId, session.PendingVolume)
                .ConfigureAwait(false);

            await _uiInvoke(() =>
            {
                session.ApplySnapshot(new ChartHubServerVolumeSessionResponse(
                    session.SessionId,
                    response.Name,
                    session.ProcessId,
                    session.ApplicationName,
                    response.ValuePercent,
                    response.IsMuted));
                StatusMessage = response.Message;
            });
        }
        catch (Exception ex)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = UiLocalization.Format("Volume.SessionUpdateFailed", ex.Message);
            });
        }
        finally
        {
            session.IsBusy = false;
        }
    }

    private void EnsureStreamStarted()
    {
        if (_streamStarted)
        {
            return;
        }

        _streamStarted = true;
        ObserveBackgroundTask(StreamUpdatesAsync(_streamCts.Token), "Volume stream");
    }

    private async Task StreamUpdatesAsync(CancellationToken cancellationToken)
    {
        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            return;
        }

        try
        {
            await foreach (ChartHubServerVolumeStateResponse state in _serverApiClient!
                               .StreamVolumeAsync(baseUrl, bearerToken, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                await ApplySnapshotAsync(state, UiLocalization.Get("Volume.StateUpdated")).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = UiLocalization.Format("Volume.StreamDisconnected", ex.Message);
            });
        }
    }

    private async Task ApplySnapshotAsync(ChartHubServerVolumeStateResponse state, string statusMessage)
    {
        await _uiInvoke(() =>
        {
            bool preservePendingMaster = PendingMasterVolume != CurrentMasterVolume;
            CurrentMasterVolume = state.Master.ValuePercent;
            if (!preservePendingMaster)
            {
                PendingMasterVolume = state.Master.ValuePercent;
            }

            MasterMuted = state.Master.IsMuted;
            SupportsPerApplicationSessions = state.SupportsPerApplicationSessions;
            SessionSupportMessage = string.IsNullOrWhiteSpace(state.SessionSupportMessage)
                ? UiLocalization.Get("Volume.PerApplicationUnsupported")
                : state.SessionSupportMessage;

            var existing = Sessions.ToDictionary(item => item.SessionId, item => item, StringComparer.Ordinal);
            ObservableCollection<VolumeSessionCardItem> updated = [];

            foreach (ChartHubServerVolumeSessionResponse session in state.Sessions.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!existing.TryGetValue(session.SessionId, out VolumeSessionCardItem? card))
                {
                    card = new VolumeSessionCardItem(session);
                }
                else
                {
                    card.ApplySnapshot(session);
                }

                updated.Add(card);
            }

            Sessions = updated;
            StatusMessage = statusMessage;
        });
    }

    private bool TryGetServerConnection(out string baseUrl, out string bearerToken)
    {
        baseUrl = _globalSettings?.ServerApiBaseUrl?.Trim() ?? string.Empty;
        bearerToken = _globalSettings?.ServerApiAuthToken?.Trim() ?? string.Empty;

        return !string.IsNullOrWhiteSpace(baseUrl)
               && !string.IsNullOrWhiteSpace(bearerToken)
               && _serverApiClient is not null;
    }

    private void OnVolumeStepRequested(object? sender, int delta)
    {
        ObserveBackgroundTask(AdjustMasterVolumeAsync(delta), "Android volume hardware step");
    }

    private void OnGlobalSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppGlobalSettings.AndroidVolumeButtonsControlServerVolume))
        {
            OnPropertyChanged(nameof(IsAndroidHardwareBindingEnabled));
        }
    }

    private static void ObserveBackgroundTask(Task task, string context)
    {
        _ = task.ContinueWith(t =>
        {
            Exception? ex = t.Exception?.GetBaseException();
            if (ex is not null)
            {
                Logger.LogError("Volume", $"{context} failed", ex);
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Dispose()
    {
        if (_hardwareButtonSource is not null)
        {
            _hardwareButtonSource.VolumeStepRequested -= OnVolumeStepRequested;
        }

        if (_globalSettings is not null)
        {
            _globalSettings.PropertyChanged -= OnGlobalSettingsPropertyChanged;
        }

        _streamCts.Cancel();
        _streamCts.Dispose();
        _masterAdjustLock.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class VolumeSessionCardItem : INotifyPropertyChanged
{
    private string _name;
    private int? _processId;
    private string? _applicationName;
    private int _currentVolume;
    private int _pendingVolume;
    private bool _isMuted;
    private bool _isBusy;

    public VolumeSessionCardItem(ChartHubServerVolumeSessionResponse session)
    {
        SessionId = session.SessionId;
        _name = session.Name;
        _processId = session.ProcessId;
        _applicationName = session.ApplicationName;
        _currentVolume = session.ValuePercent;
        _pendingVolume = session.ValuePercent;
        _isMuted = session.IsMuted;
    }

    public string SessionId { get; }

    public string Name
    {
        get => _name;
        private set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Subtitle));
        }
    }

    public int? ProcessId
    {
        get => _processId;
        private set
        {
            if (_processId == value)
            {
                return;
            }

            _processId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Subtitle));
        }
    }

    public string? ApplicationName
    {
        get => _applicationName;
        private set
        {
            if (_applicationName == value)
            {
                return;
            }

            _applicationName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Subtitle));
        }
    }

    public string Subtitle => ProcessId.HasValue
        ? $"{ApplicationName ?? Name} | PID {ProcessId.Value}"
        : (ApplicationName ?? Name);

    public int CurrentVolume
    {
        get => _currentVolume;
        private set
        {
            if (_currentVolume == value)
            {
                return;
            }

            _currentVolume = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(CurrentLabel));
        }
    }

    public int PendingVolume
    {
        get => _pendingVolume;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (_pendingVolume == clamped)
            {
                return;
            }

            _pendingVolume = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(PendingLabel));
        }
    }

    public string PendingLabel => UiLocalization.Format("Volume.PendingPercent", PendingVolume);

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
            OnPropertyChanged(nameof(CurrentLabel));
        }
    }

    public string CurrentLabel => IsMuted
        ? UiLocalization.Get("Common.Muted")
        : UiLocalization.Format("Common.CurrentPercent", CurrentVolume);

    public bool IsDirty => PendingVolume != CurrentVolume;

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public void ApplySnapshot(ChartHubServerVolumeSessionResponse session)
    {
        bool preservePending = PendingVolume != CurrentVolume;

        Name = session.Name;
        ProcessId = session.ProcessId;
        ApplicationName = session.ApplicationName;
        CurrentVolume = session.ValuePercent;
        if (!preservePending)
        {
            PendingVolume = session.ValuePercent;
        }

        IsMuted = session.IsMuted;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}