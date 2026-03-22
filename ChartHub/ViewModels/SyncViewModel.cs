using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

namespace ChartHub.ViewModels;

public sealed class SyncViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly JsonSerializerOptions ProfileJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly TimeSpan DefaultAutoRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly IDesktopSyncApiClient _desktopSyncApiClient;
    private readonly AppGlobalSettings _appGlobalSettings;
    private readonly TimeSpan _autoRefreshInterval;

    private string _desktopApiBaseUrl = "http://127.0.0.1:15123";
    private string _syncToken = string.Empty;
    private string _statusMessage = "Enter desktop host URL and token, then test connection.";
    private string? _errorMessage;
    private bool _isBusy;
    private bool _isConnected;
    private IngestionQueueItem? _selectedItem;
    private string _deviceLabel = "Android Companion";
    private string _pairCode = string.Empty;
    private SyncConnectionProfile? _selectedProfile;
    private CancellationTokenSource? _autoRefreshCancellationTokenSource;
    private DateTimeOffset? _lastAutoRefreshUtc;
    private ConnectionDiagnostics _diagnostics = new();

    public string DesktopApiBaseUrl
    {
        get => _desktopApiBaseUrl;
        set
        {
            if (_desktopApiBaseUrl == value)
            {
                return;
            }

            _desktopApiBaseUrl = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionHint));
            OnPropertyChanged(nameof(HasConnectionHint));
            SaveProfileCommand.NotifyCanExecuteChanged();
            PairWithCodeCommand.NotifyCanExecuteChanged();
        }
    }

    public string SyncToken
    {
        get => _syncToken;
        set
        {
            if (_syncToken == value)
            {
                return;
            }

            _syncToken = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionHint));
            OnPropertyChanged(nameof(HasConnectionHint));
            SaveProfileCommand.NotifyCanExecuteChanged();
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

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value)
            {
                return;
            }

            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string ConnectionHint
    {
        get
        {
            if (IsBusy)
            {
                return "Working...";
            }

            if (IsConnected)
            {
                return "Connected. Use Refresh Queue to pull desktop ingestion updates.";
            }

            if (HasError)
            {
                return "Connection failed. Verify desktop URL and credentials, then retry.";
            }

            if (string.IsNullOrWhiteSpace(SyncToken))
            {
                return "Use Pair + Connect with the desktop pair code, or paste a sync token then Connect.";
            }

            return "Press Connect to validate token and load the desktop queue.";
        }
    }

    public bool HasConnectionHint => !string.IsNullOrWhiteSpace(ConnectionHint);

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
            OnPropertyChanged(nameof(ConnectionHint));
            OnPropertyChanged(nameof(HasConnectionHint));
            OnPropertyChanged(nameof(ShowEmptyQueueState));
            TestConnectionCommand.NotifyCanExecuteChanged();
            PairWithCodeCommand.NotifyCanExecuteChanged();
            RefreshQueueCommand.NotifyCanExecuteChanged();
            RetrySelectedCommand.NotifyCanExecuteChanged();
            InstallSelectedCommand.NotifyCanExecuteChanged();
            OpenFolderSelectedCommand.NotifyCanExecuteChanged();
        }
    }

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
            OnPropertyChanged(nameof(ConnectionHint));
            OnPropertyChanged(nameof(HasConnectionHint));
            OnPropertyChanged(nameof(ShowEmptyQueueState));
            OnPropertyChanged(nameof(AutoRefreshHint));
            OnPropertyChanged(nameof(IsAutoRefreshActive));
            RefreshQueueCommand.NotifyCanExecuteChanged();
            RetrySelectedCommand.NotifyCanExecuteChanged();
            InstallSelectedCommand.NotifyCanExecuteChanged();
            OpenFolderSelectedCommand.NotifyCanExecuteChanged();

            if (_isConnected)
            {
                StartAutoRefreshLoop();
            }
            else
            {
                StopAutoRefreshLoop();
            }
        }
    }

    public IngestionQueueItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem == value)
            {
                return;
            }

            _selectedItem = value;
            OnPropertyChanged();
            RetrySelectedCommand.NotifyCanExecuteChanged();
            InstallSelectedCommand.NotifyCanExecuteChanged();
            OpenFolderSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    public string DeviceLabel
    {
        get => _deviceLabel;
        set
        {
            if (_deviceLabel == value)
            {
                return;
            }

            _deviceLabel = value;
            OnPropertyChanged();
            SaveProfileCommand.NotifyCanExecuteChanged();
            PairWithCodeCommand.NotifyCanExecuteChanged();
        }
    }

    public string PairCode
    {
        get => _pairCode;
        set
        {
            if (_pairCode == value)
            {
                return;
            }

            _pairCode = value;
            OnPropertyChanged();
            SaveProfileCommand.NotifyCanExecuteChanged();
            PairWithCodeCommand.NotifyCanExecuteChanged();
        }
    }

    public SyncConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (_selectedProfile == value)
            {
                return;
            }

            _selectedProfile = value;
            OnPropertyChanged();
            ApplyProfileCommand.NotifyCanExecuteChanged();
            RemoveProfileCommand.NotifyCanExecuteChanged();
        }
    }

    public ObservableCollection<IngestionQueueItem> QueueItems { get; } = [];
    public ObservableCollection<SyncConnectionProfile> SavedProfiles { get; } = [];

    public bool HasQueueItems => QueueItems.Count > 0;
    public bool ShowEmptyQueueState => IsConnected && !IsBusy && QueueItems.Count == 0;
    public bool IsAutoRefreshActive => IsConnected && _autoRefreshCancellationTokenSource is not null;
    public string AutoRefreshHint => !IsConnected
        ? "Auto-refresh starts after a successful connection."
        : _lastAutoRefreshUtc is null
            ? $"Auto-refresh enabled every {(int)_autoRefreshInterval.TotalSeconds}s."
            : $"Auto-refresh every {(int)_autoRefreshInterval.TotalSeconds}s. Last sync: {_lastAutoRefreshUtc.Value.ToLocalTime():HH:mm:ss}";

    public ConnectionDiagnostics Diagnostics
    {
        get => _diagnostics;
        private set
        {
            if (ReferenceEquals(_diagnostics, value))
            {
                return;
            }

            _diagnostics = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDiagnosticsToDisplay));
            OnPropertyChanged(nameof(DiagnosticsSummary));
        }
    }

    public bool HasDiagnosticsToDisplay => !string.IsNullOrWhiteSpace(Diagnostics.LastErrorMessage) || Diagnostics.ServerInfo is not null;
    public string DiagnosticsSummary => Diagnostics.DiagnosticsSummary;

    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IAsyncRelayCommand PairWithCodeCommand { get; }
    public IAsyncRelayCommand RefreshQueueCommand { get; }
    public IAsyncRelayCommand<IngestionQueueItem?> RetrySelectedCommand { get; }
    public IAsyncRelayCommand<IngestionQueueItem?> InstallSelectedCommand { get; }
    public IAsyncRelayCommand<IngestionQueueItem?> OpenFolderSelectedCommand { get; }
    public IRelayCommand SaveProfileCommand { get; }
    public IRelayCommand<SyncConnectionProfile?> ApplyProfileCommand { get; }
    public IRelayCommand<SyncConnectionProfile?> RemoveProfileCommand { get; }

    public SyncViewModel(IDesktopSyncApiClient desktopSyncApiClient, AppGlobalSettings appGlobalSettings, TimeSpan? autoRefreshInterval = null)
    {
        _desktopSyncApiClient = desktopSyncApiClient;
        _appGlobalSettings = appGlobalSettings;
        _autoRefreshInterval = autoRefreshInterval.GetValueOrDefault(DefaultAutoRefreshInterval);

        _desktopApiBaseUrl = _appGlobalSettings.SyncApiDesktopBaseUrl;
        _deviceLabel = _appGlobalSettings.SyncApiDeviceLabel;
        _pairCode = _appGlobalSettings.SyncApiPairCode;

        if (!string.IsNullOrWhiteSpace(_appGlobalSettings.SyncApiAuthToken))
        {
            _syncToken = _appGlobalSettings.SyncApiAuthToken;
        }

        QueueItems.CollectionChanged += OnQueueItemsCollectionChanged;
        LoadProfiles();

        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanTestConnection);
        PairWithCodeCommand = new AsyncRelayCommand(PairWithCodeAsync, CanPairWithCode);
        RefreshQueueCommand = new AsyncRelayCommand(RefreshQueueAsync, CanRefreshQueue);
        RetrySelectedCommand = new AsyncRelayCommand<IngestionQueueItem?>(RetrySelectedAsync, CanActOnSelectedItem);
        InstallSelectedCommand = new AsyncRelayCommand<IngestionQueueItem?>(InstallSelectedAsync, CanActOnSelectedItem);
        OpenFolderSelectedCommand = new AsyncRelayCommand<IngestionQueueItem?>(OpenFolderSelectedAsync, CanActOnSelectedItem);
        SaveProfileCommand = new RelayCommand(SaveCurrentProfile, CanSaveProfile);
        ApplyProfileCommand = new RelayCommand<SyncConnectionProfile?>(ApplyProfile, CanUseProfile);
        RemoveProfileCommand = new RelayCommand<SyncConnectionProfile?>(RemoveProfile, CanUseProfile);
    }

    private bool CanTestConnection()
    {
        return !IsBusy;
    }

    private bool CanRefreshQueue()
    {
        return !IsBusy && IsConnected;
    }

    private bool CanPairWithCode()
    {
        return !IsBusy
            && !string.IsNullOrWhiteSpace(DesktopApiBaseUrl)
            && !string.IsNullOrWhiteSpace(PairCode);
    }

    private bool CanActOnSelectedItem(IngestionQueueItem? item)
    {
        return !IsBusy && IsConnected && item is not null;
    }

    private bool CanSaveProfile()
    {
        return !IsBusy
            && !string.IsNullOrWhiteSpace(DesktopApiBaseUrl)
            && !string.IsNullOrWhiteSpace(SyncToken)
            && !string.IsNullOrWhiteSpace(DeviceLabel);
    }

    private bool CanUseProfile(SyncConnectionProfile? profile)
    {
        return !IsBusy && profile is not null;
    }

    private async Task TestConnectionAsync()
    {
        await RunOperationAsync(async () =>
        {
            await ConnectAndRefreshAsync();
        }, "Desktop sync connection failed");
    }

    private async Task PairWithCodeAsync()
    {
        await RunOperationAsync(async () =>
        {
            DesktopSyncPairClaimResponse claim = await _desktopSyncApiClient.ClaimPairTokenAsync(DesktopApiBaseUrl, PairCode, DeviceLabel);
            if (!claim.Paired || string.IsNullOrWhiteSpace(claim.Token))
            {
                throw new InvalidOperationException("Pairing did not return a usable token.");
            }

            SyncToken = claim.Token;
            if (!string.IsNullOrWhiteSpace(claim.ApiBaseUrl))
            {
                DesktopApiBaseUrl = claim.ApiBaseUrl;
            }

            await ConnectAndRefreshAsync();
        }, "Pair-code handshake failed");
    }

    private async Task ConnectAndRefreshAsync()
    {
        DesktopSyncVersionResponse version = await _desktopSyncApiClient.GetVersionAsync(DesktopApiBaseUrl, SyncToken);
        if (!version.SupportsIngestions)
        {
            throw new InvalidOperationException("Desktop host does not support ingestion sync.");
        }

        IsConnected = true;
        _appGlobalSettings.SyncApiAuthToken = SyncToken;
        _appGlobalSettings.SyncApiDesktopBaseUrl = DesktopApiBaseUrl;
        _appGlobalSettings.SyncApiDeviceLabel = DeviceLabel;
        _appGlobalSettings.SyncApiPairCode = PairCode;

        StatusMessage = $"Connected to {version.Api} v{version.Version}.";
        ErrorMessage = null;

        // Update diagnostics with successful connection
        Diagnostics = new ConnectionDiagnostics
        {
            LastAttemptUtc = DateTime.UtcNow,
            ServerInfo = $"{version.Api} v{version.Version}",
            LastErrorMessage = null,
            LastErrorCategory = null,
            RemediationHint = null
        };

        await RefreshQueueCoreAsync();
    }

    private async Task RefreshQueueAsync()
    {
        await RunOperationAsync(RefreshQueueCoreAsync, "Failed to refresh ingestion queue");
    }

    private async Task RefreshQueueCoreAsync()
    {
        IReadOnlyList<IngestionQueueItem> items = await _desktopSyncApiClient.GetIngestionsAsync(DesktopApiBaseUrl, SyncToken, limit: 200);

        QueueItems.Clear();
        foreach (IngestionQueueItem item in items)
        {
            QueueItems.Add(item);
        }

        StatusMessage = QueueItems.Count == 0
            ? "Connected. No ingestion items currently queued."
            : $"Loaded {QueueItems.Count} ingestion item(s).";
        ErrorMessage = null;

        if (IsConnected)
        {
            _lastAutoRefreshUtc = DateTimeOffset.UtcNow;
            OnPropertyChanged(nameof(AutoRefreshHint));
        }
    }

    private void StartAutoRefreshLoop()
    {
        StopAutoRefreshLoop();

        _autoRefreshCancellationTokenSource = new CancellationTokenSource();
        CancellationToken cancellationToken = _autoRefreshCancellationTokenSource.Token;

        _ = Task.Run(async () =>
        {
            await AutoRefreshLoopAsync(cancellationToken);
        }, cancellationToken);

        OnPropertyChanged(nameof(IsAutoRefreshActive));
    }

    private void StopAutoRefreshLoop()
    {
        _autoRefreshCancellationTokenSource?.Cancel();
        _autoRefreshCancellationTokenSource?.Dispose();
        _autoRefreshCancellationTokenSource = null;
        OnPropertyChanged(nameof(IsAutoRefreshActive));
    }

    private async Task AutoRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_autoRefreshInterval, cancellationToken);

                if (cancellationToken.IsCancellationRequested || !IsConnected || IsBusy)
                {
                    continue;
                }

                await RefreshQueueAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping auto-refresh.
        }
    }

    private async Task RetrySelectedAsync(IngestionQueueItem? item)
    {
        if (item is null)
        {
            return;
        }

        DateTimeOffset initiatedAt = DateTimeOffset.UtcNow;
        item.LastActionResult = new ActionResult
        {
            ActionType = ActionType.Retry,
            Status = ActionResultStatus.Pending,
            Message = "Processing...",
            InitiatedAtUtc = initiatedAt,
            CompletedAtUtc = initiatedAt
        };

        await RunOperationAsync(async () =>
        {
            try
            {
                await _desktopSyncApiClient.TriggerRetryAsync(DesktopApiBaseUrl, SyncToken, item.IngestionId);
                item.LastActionResult = new ActionResult
                {
                    ActionType = ActionType.Retry,
                    Status = ActionResultStatus.Success,
                    Message = "Retry requested successfully",
                    InitiatedAtUtc = initiatedAt,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                };
                StatusMessage = $"Retry requested for ingestion {item.IngestionId}.";
                ErrorMessage = null;
                await RefreshQueueCoreAsync();
            }
            catch (Exception ex)
            {
                item.LastActionResult = new ActionResult
                {
                    ActionType = ActionType.Retry,
                    Status = ActionResultStatus.Failed,
                    Message = ex.Message,
                    InitiatedAtUtc = initiatedAt,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                };
                throw;
            }
        }, "Failed to trigger retry");
    }

    private async Task InstallSelectedAsync(IngestionQueueItem? item)
    {
        if (item is null)
        {
            return;
        }

        DateTimeOffset initiatedAt = DateTimeOffset.UtcNow;
        item.LastActionResult = new ActionResult
        {
            ActionType = ActionType.Install,
            Status = ActionResultStatus.Pending,
            Message = "Processing...",
            InitiatedAtUtc = initiatedAt,
            CompletedAtUtc = initiatedAt
        };

        await RunOperationAsync(async () =>
        {
            try
            {
                await _desktopSyncApiClient.TriggerInstallAsync(DesktopApiBaseUrl, SyncToken, item.IngestionId);
                item.LastActionResult = new ActionResult
                {
                    ActionType = ActionType.Install,
                    Status = ActionResultStatus.Success,
                    Message = "Install requested successfully",
                    InitiatedAtUtc = initiatedAt,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                };
                StatusMessage = $"Install requested for ingestion {item.IngestionId}.";
                ErrorMessage = null;
                await RefreshQueueCoreAsync();
            }
            catch (Exception ex)
            {
                item.LastActionResult = new ActionResult
                {
                    ActionType = ActionType.Install,
                    Status = ActionResultStatus.Failed,
                    Message = ex.Message,
                    InitiatedAtUtc = initiatedAt,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                };
                throw;
            }
        }, "Failed to trigger install");
    }

    private async Task OpenFolderSelectedAsync(IngestionQueueItem? item)
    {
        if (item is null)
        {
            return;
        }

        DateTimeOffset initiatedAt = DateTimeOffset.UtcNow;
        item.LastActionResult = new ActionResult
        {
            ActionType = ActionType.OpenFolder,
            Status = ActionResultStatus.Pending,
            Message = "Processing...",
            InitiatedAtUtc = initiatedAt,
            CompletedAtUtc = initiatedAt
        };

        await RunOperationAsync(async () =>
        {
            try
            {
                await _desktopSyncApiClient.TriggerOpenFolderAsync(DesktopApiBaseUrl, SyncToken, item.IngestionId);
                item.LastActionResult = new ActionResult
                {
                    ActionType = ActionType.OpenFolder,
                    Status = ActionResultStatus.Success,
                    Message = "Folder opened",
                    InitiatedAtUtc = initiatedAt,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                };
                StatusMessage = $"Open folder requested for ingestion {item.IngestionId}.";
                ErrorMessage = null;
            }
            catch (Exception ex)
            {
                item.LastActionResult = new ActionResult
                {
                    ActionType = ActionType.OpenFolder,
                    Status = ActionResultStatus.Failed,
                    Message = ex.Message,
                    InitiatedAtUtc = initiatedAt,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                };
                throw;
            }
        }, "Failed to request folder open");
    }

    private void SaveCurrentProfile()
    {
        if (!CanSaveProfile())
        {
            return;
        }

        string name = DeviceLabel.Trim();
        SyncConnectionProfile? existing = SavedProfiles.FirstOrDefault(profile =>
            profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        var profile = new SyncConnectionProfile(
            Name: name,
            BaseUrl: DesktopApiBaseUrl.Trim(),
            Token: SyncToken.Trim(),
            PairCode: PairCode.Trim(),
            LastConnectedUtc: DateTimeOffset.UtcNow);

        if (existing is null)
        {
            SavedProfiles.Add(profile);
        }
        else
        {
            int index = SavedProfiles.IndexOf(existing);
            SavedProfiles[index] = profile;
        }

        SelectedProfile = profile;
        PersistProfiles();
        StatusMessage = $"Saved connection profile '{profile.Name}'.";
        ErrorMessage = null;
    }

    private void ApplyProfile(SyncConnectionProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        DeviceLabel = profile.Name;
        DesktopApiBaseUrl = profile.BaseUrl;
        SyncToken = profile.Token;
        PairCode = profile.PairCode;

        _appGlobalSettings.SyncApiDeviceLabel = DeviceLabel;
        _appGlobalSettings.SyncApiDesktopBaseUrl = DesktopApiBaseUrl;
        _appGlobalSettings.SyncApiAuthToken = SyncToken;
        _appGlobalSettings.SyncApiPairCode = PairCode;

        StatusMessage = $"Applied profile '{profile.Name}'.";
        ErrorMessage = null;
    }

    private void RemoveProfile(SyncConnectionProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        SavedProfiles.Remove(profile);
        if (ReferenceEquals(SelectedProfile, profile))
        {
            SelectedProfile = null;
        }

        PersistProfiles();
        StatusMessage = $"Removed profile '{profile.Name}'.";
        ErrorMessage = null;
    }

    private void LoadProfiles()
    {
        string json = _appGlobalSettings.SyncApiSavedConnectionsJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            List<SyncConnectionProfile> items = JsonSerializer.Deserialize<List<SyncConnectionProfile>>(json, ProfileJsonOptions) ?? [];
            SavedProfiles.Clear();
            foreach (SyncConnectionProfile? item in items.Where(static item => !string.IsNullOrWhiteSpace(item.Name)))
            {
                SavedProfiles.Add(item);
            }
        }
        catch
        {
            SavedProfiles.Clear();
        }
    }

    private void PersistProfiles()
    {
        string json = JsonSerializer.Serialize(SavedProfiles.ToList(), ProfileJsonOptions);
        _appGlobalSettings.SyncApiSavedConnectionsJson = json;
    }

    private async Task RunOperationAsync(Func<Task> operation, string context)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            IsConnected = false;
            QueueItems.Clear();
            _lastAutoRefreshUtc = null;
            ErrorMessage = ex.Message;
            StatusMessage = "Connection failed. Verify desktop URL and credentials, then retry.";
            OnPropertyChanged(nameof(AutoRefreshHint));

            // Classify error and update diagnostics
            (ErrorCategory category, string? remediation) = ClassifyError(ex);
            Diagnostics = new ConnectionDiagnostics
            {
                LastAttemptUtc = DateTime.UtcNow,
                ServerInfo = null,
                LastErrorMessage = ex.Message,
                LastErrorCategory = category,
                RemediationHint = remediation
            };

            Logger.LogError("Sync", context, ex, new Dictionary<string, object?>
            {
                ["desktopApiBaseUrl"] = DesktopApiBaseUrl,
                ["queueSize"] = QueueItems.Count,
                ["errorCategory"] = category.ToString(),
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private (ErrorCategory category, string remediation) ClassifyError(Exception ex)
    {
        string message = ex.Message.ToLowerInvariant();

        if (message.Contains("not found") || message.Contains("unreachable") || message.Contains("connection refused")
            || message.Contains("timeout") || message.Contains("no such host"))
        {
            return (ErrorCategory.NetworkUnreachable, "Verify desktop URL (e.g., http://192.168.1.10:15123) is correct and reachable.");
        }

        if (message.Contains("unauthorized") || message.Contains("invalid token") || message.Contains("authentication"))
        {
            return (ErrorCategory.AuthenticationFailed, "Regenerate token in Settings or re-pair with desktop.");
        }

        if (message.Contains("does not support") || message.Contains("ingestion") || ex is InvalidOperationException)
        {
            return (ErrorCategory.UnsupportedVersion, "Upgrade desktop host or check compatibility.");
        }

        return (ErrorCategory.UnknownError, "Check error message above and retry.");
    }

    private void OnQueueItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasQueueItems));
        OnPropertyChanged(nameof(ShowEmptyQueueState));
    }

    public void Dispose()
    {
        QueueItems.CollectionChanged -= OnQueueItemsCollectionChanged;
        StopAutoRefreshLoop();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record SyncConnectionProfile(
    string Name,
    string BaseUrl,
    string Token,
    string PairCode,
    DateTimeOffset LastConnectedUtc)
{
    public string UpdatedText => LastConnectedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}