using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;

using Avalonia.Media.Imaging;
using Avalonia.Threading;

using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

using QRCoder;

namespace ChartHub.ViewModels;

public sealed class SyncViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly JsonSerializerOptions SyncJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly TimeSpan DefaultAutoRefreshInterval = TimeSpan.FromSeconds(15);
    private const string DefaultDesktopBootstrapLabel = "ChartHub Desktop";
    private const string DefaultHostListenPrefix = "http://0.0.0.0:15123/";

    private readonly IDesktopSyncApiClient _desktopSyncApiClient;
    private readonly IIngestionSyncApiHost? _ingestionSyncApiHost;
    private readonly ILocalIngestionPushService _localIngestionPushService;
    private readonly ILocalDownloadFileCatalogService _localDownloadFileCatalogService;
    private readonly IQrCodeScannerService _qrCodeScannerService;
    private readonly IDesktopSyncQrImageExportService _desktopSyncQrImageExportService;
    private readonly AppGlobalSettings _appGlobalSettings;
    private readonly TimeSpan _autoRefreshInterval;
    private readonly bool _isCompanionMode;

    private string _desktopApiBaseUrl = "http://127.0.0.1:15123";
    private string _syncToken = string.Empty;
    private string _statusMessage = string.Empty;
    private string? _errorMessage;
    private bool _isBusy;
    private bool _isConnected;
    private IngestionQueueItem? _selectedItem;
    private string _deviceLabel = "Android Companion";
    private string _pairCode = string.Empty;
    private string _generatedBootstrapPayload = string.Empty;
    private PendingQrPairingRequest? _pendingQrPairing;
    private Bitmap? _bootstrapQrImage;
    private string? _bootstrapQrImageExportPath;
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
            RegenerateBootstrapQrPayload();
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
            if (IsDesktopMode)
            {
                if (string.IsNullOrWhiteSpace(PairCode))
                {
                    return "Set a pair code in Settings to generate the companion QR code.";
                }

                return HasBootstrapQrImage
                    ? "Scan this QR on the companion, then confirm pairing on the phone."
                    : "Configure a LAN-reachable desktop sync address to generate the companion QR code.";
            }

            if (IsBusy)
            {
                return "Working...";
            }

            if (IsConnected)
            {
                return "Connected. The queue refreshes automatically while the desktop stays reachable.";
            }

            if (HasPendingQrPairing)
            {
                return "QR scanned. Confirm pairing to connect to the desktop over LAN.";
            }

            if (HasError)
            {
                return "Pairing failed. Scan the desktop QR and try again.";
            }

            return "Scan the desktop QR to start pairing.";
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
            OnPropertyChanged(nameof(ShowCompanionScanSection));
            OnPropertyChanged(nameof(ShowCompanionConfirmationSection));
            TestConnectionCommand.NotifyCanExecuteChanged();
            ConfirmQrPairingCommand.NotifyCanExecuteChanged();
            CancelPendingQrPairingCommand.NotifyCanExecuteChanged();
            ScanBootstrapQrCommand.NotifyCanExecuteChanged();
            RefreshDesktopQrCommand.NotifyCanExecuteChanged();
            RefreshQueueCommand.NotifyCanExecuteChanged();
            RefreshLocalFilesCommand.NotifyCanExecuteChanged();
            PushSelectedLocalFilesCommand.NotifyCanExecuteChanged();
            RetrySelectedCommand.NotifyCanExecuteChanged();
            InstallSelectedCommand.NotifyCanExecuteChanged();
            PushSelectedCommand.NotifyCanExecuteChanged();
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
            OnPropertyChanged(nameof(ShowCompanionScanSection));
            OnPropertyChanged(nameof(ShowCompanionConfirmationSection));
            OnPropertyChanged(nameof(ShowCompanionQueueSection));
            OnPropertyChanged(nameof(ShowCompanionLocalFilesSection));
            OnPropertyChanged(nameof(ShowNoLocalFilesHint));
            OnPropertyChanged(nameof(AutoRefreshHint));
            OnPropertyChanged(nameof(IsAutoRefreshActive));
            RefreshQueueCommand.NotifyCanExecuteChanged();
            RefreshLocalFilesCommand.NotifyCanExecuteChanged();
            PushSelectedLocalFilesCommand.NotifyCanExecuteChanged();
            RetrySelectedCommand.NotifyCanExecuteChanged();
            InstallSelectedCommand.NotifyCanExecuteChanged();
            PushSelectedCommand.NotifyCanExecuteChanged();
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
            PushSelectedCommand.NotifyCanExecuteChanged();
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
            RegenerateBootstrapQrPayload();
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
            RegenerateBootstrapQrPayload();
        }
    }

    public string GeneratedBootstrapPayload
    {
        get => _generatedBootstrapPayload;
        private set
        {
            if (_generatedBootstrapPayload == value)
            {
                return;
            }

            _generatedBootstrapPayload = value;
            OnPropertyChanged();
        }
    }

    public string PendingQrPairingSummary => _pendingQrPairing is null
        ? string.Empty
        : string.IsNullOrWhiteSpace(_pendingQrPairing.DesktopLabel)
            ? _pendingQrPairing.ApiBaseUrl
            : $"{_pendingQrPairing.DesktopLabel} ({_pendingQrPairing.ApiBaseUrl})";

    public bool HasPendingQrPairing => _pendingQrPairing is not null;

    public Bitmap? BootstrapQrImage
    {
        get => _bootstrapQrImage;
        private set
        {
            if (ReferenceEquals(_bootstrapQrImage, value))
            {
                return;
            }

            _bootstrapQrImage?.Dispose();
            _bootstrapQrImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasBootstrapQrImage));
            OnPropertyChanged(nameof(ShowDesktopBootstrapPlaceholder));
            OnPropertyChanged(nameof(ConnectionHint));
            OnPropertyChanged(nameof(HasConnectionHint));
        }
    }

    public string? BootstrapQrImageExportPath
    {
        get => _bootstrapQrImageExportPath;
        private set
        {
            if (_bootstrapQrImageExportPath == value)
            {
                return;
            }

            _bootstrapQrImageExportPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasBootstrapQrImageExportPath));
        }
    }

    public ObservableCollection<IngestionQueueItem> QueueItems { get; } = [];
    public ObservableCollection<WatcherFile> LocalFiles { get; } = [];

    public bool IsCompanionMode => _isCompanionMode;
    public bool IsDesktopMode => !_isCompanionMode;
    public bool HasQueueItems => QueueItems.Count > 0;
    public bool HasLocalFiles => LocalFiles.Count > 0;
    public bool HasSelectedLocalFiles => LocalFiles.Any(file => file.Checked);
    public bool HasBootstrapQrImage => BootstrapQrImage is not null;
    public bool HasBootstrapQrImageExportPath => !string.IsNullOrWhiteSpace(BootstrapQrImageExportPath);
    public bool SupportsCameraScanning => _qrCodeScannerService.IsSupported;
    public bool ShowEmptyQueueState => IsConnected && !IsBusy && QueueItems.Count == 0;
    public bool IsAutoRefreshActive => IsConnected && _autoRefreshCancellationTokenSource is not null;
    public bool ShowDesktopBootstrapSection => IsDesktopMode;
    public bool ShowCompanionScanSection => IsCompanionMode && !IsConnected;
    public bool ShowCompanionConfirmationSection => IsCompanionMode && !IsConnected && HasPendingQrPairing;
    public bool ShowCompanionQueueSection => IsCompanionMode && IsConnected;
    public bool ShowCompanionLocalFilesSection => IsCompanionMode && IsConnected;
    public bool ShowNoLocalFilesHint => IsCompanionMode && IsConnected && !HasLocalFiles;
    public bool ShowDesktopBootstrapPlaceholder => IsDesktopMode && !HasBootstrapQrImage;
    public string SyncTitle => IsDesktopMode ? "Desktop Sync Host" : "Desktop Sync Companion";
    public string SyncSubtitle => IsDesktopMode
        ? "Keep the desktop app open and present this QR to the companion. The QR must resolve to a LAN-reachable desktop address."
        : "Scan the desktop QR, confirm the pairing, then the queue opens automatically.";
    public string BootstrapInstructions => IsDesktopMode
        ? "Scan this QR on the companion. The raw payload stays visible below for debugging only."
        : string.Empty;
    public string BootstrapPlaceholderText => IsDesktopMode
        ? "Set a pair code and configure a LAN-reachable desktop sync address to generate the companion QR."
        : string.Empty;
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
    public IAsyncRelayCommand ConfirmQrPairingCommand { get; }
    public IAsyncRelayCommand ScanBootstrapQrCommand { get; }
    public IRelayCommand CancelPendingQrPairingCommand { get; }
    public IRelayCommand RefreshDesktopQrCommand { get; }
    public IAsyncRelayCommand RefreshQueueCommand { get; }
    public IAsyncRelayCommand RefreshLocalFilesCommand { get; }
    public IAsyncRelayCommand PushSelectedLocalFilesCommand { get; }
    public IAsyncRelayCommand<IngestionQueueItem?> RetrySelectedCommand { get; }
    public IAsyncRelayCommand<IngestionQueueItem?> InstallSelectedCommand { get; }
    public IAsyncRelayCommand<IngestionQueueItem?> PushSelectedCommand { get; }
    public IAsyncRelayCommand<IngestionQueueItem?> OpenFolderSelectedCommand { get; }

    public SyncViewModel(
        IDesktopSyncApiClient desktopSyncApiClient,
        AppGlobalSettings appGlobalSettings,
        IQrCodeScannerService? qrCodeScannerService = null,
        TimeSpan? autoRefreshInterval = null,
        bool? isCompanionMode = null,
        ILocalIngestionPushService? localIngestionPushService = null,
        ILocalDownloadFileCatalogService? localDownloadFileCatalogService = null,
        IDesktopSyncQrImageExportService? desktopSyncQrImageExportService = null,
        IIngestionSyncApiHost? ingestionSyncApiHost = null)
    {
        _desktopSyncApiClient = desktopSyncApiClient;
        _ingestionSyncApiHost = ingestionSyncApiHost;
        _qrCodeScannerService = qrCodeScannerService ?? new NoOpQrCodeScannerService();
        _desktopSyncQrImageExportService = desktopSyncQrImageExportService ?? new DesktopSyncQrImageExportService();
        _appGlobalSettings = appGlobalSettings;
        _autoRefreshInterval = autoRefreshInterval.GetValueOrDefault(DefaultAutoRefreshInterval);
        _isCompanionMode = isCompanionMode ?? OperatingSystem.IsAndroid();
        _localIngestionPushService = localIngestionPushService ?? new NoOpLocalIngestionPushService();
        _localDownloadFileCatalogService = localDownloadFileCatalogService ?? new LocalDownloadFileCatalogService();

        _deviceLabel = _appGlobalSettings.SyncApiDeviceLabel;
        _pairCode = _appGlobalSettings.SyncApiPairCode;
        _statusMessage = GetInitialStatusMessage();

        if (!string.IsNullOrWhiteSpace(_appGlobalSettings.SyncApiAuthToken))
        {
            _syncToken = _appGlobalSettings.SyncApiAuthToken;
        }

        QueueItems.CollectionChanged += OnQueueItemsCollectionChanged;
        LocalFiles.CollectionChanged += OnLocalFilesCollectionChanged;
        _appGlobalSettings.PropertyChanged += OnAppGlobalSettingsPropertyChanged;
        RegenerateBootstrapQrPayload();

        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanTestConnection);
        ConfirmQrPairingCommand = new AsyncRelayCommand(ConfirmQrPairingAsync, CanConfirmQrPairing);
        ScanBootstrapQrCommand = new AsyncRelayCommand(ScanBootstrapQrAsync, CanScanBootstrapQr);
        CancelPendingQrPairingCommand = new RelayCommand(CancelPendingQrPairing, CanCancelPendingQrPairing);
        RefreshDesktopQrCommand = new RelayCommand(RefreshDesktopQr, CanRefreshDesktopQr);
        RefreshQueueCommand = new AsyncRelayCommand(RefreshQueueAsync, CanRefreshQueue);
        RefreshLocalFilesCommand = new AsyncRelayCommand(RefreshLocalFilesAsync, CanRefreshQueue);
        PushSelectedLocalFilesCommand = new AsyncRelayCommand(PushSelectedLocalFilesAsync, CanPushSelectedLocalFiles);
        RetrySelectedCommand = new AsyncRelayCommand<IngestionQueueItem?>(RetrySelectedAsync, CanActOnSelectedItem);
        InstallSelectedCommand = new AsyncRelayCommand<IngestionQueueItem?>(InstallSelectedAsync, CanInstallSelectedItem);
        PushSelectedCommand = new AsyncRelayCommand<IngestionQueueItem?>(PushSelectedAsync, CanPushSelectedItem);
        OpenFolderSelectedCommand = new AsyncRelayCommand<IngestionQueueItem?>(OpenFolderSelectedAsync, CanActOnSelectedItem);
    }

    private bool CanTestConnection()
    {
        return !IsBusy;
    }

    private string GetInitialStatusMessage()
    {
        return IsDesktopMode
            ? "Present this QR on the desktop so the companion can pair over LAN."
            : "Scan the desktop QR to start pairing.";
    }

    private bool CanRefreshQueue()
    {
        return !IsBusy && IsConnected;
    }

    private bool CanRefreshDesktopQr()
    {
        return IsDesktopMode && !IsBusy;
    }

    private bool CanPushSelectedLocalFiles()
    {
        return !IsBusy && IsConnected && HasSelectedLocalFiles;
    }

    private bool CanInstallSelectedItem(IngestionQueueItem? item)
    {
        return CanActOnSelectedItem(item)
            && item?.CanInstall == true;
    }

    private bool CanActOnSelectedItem(IngestionQueueItem? item)
    {
        return !IsBusy && IsConnected && item is not null;
    }

    private bool CanPushSelectedItem(IngestionQueueItem? item)
    {
        return CanActOnSelectedItem(item)
            && !string.IsNullOrWhiteSpace(item?.DownloadedLocation);
    }

    private bool CanScanBootstrapQr()
    {
        return !IsBusy && SupportsCameraScanning;
    }

    private bool CanConfirmQrPairing()
    {
        return !IsBusy && _pendingQrPairing is not null;
    }

    private bool CanCancelPendingQrPairing()
    {
        return !IsBusy && _pendingQrPairing is not null;
    }

    private async Task ScanBootstrapQrAsync()
    {
        await RunOperationAsync(async () =>
        {
            string? scannedPayload = await _qrCodeScannerService.ScanAsync();
            string normalizedPayload = scannedPayload?.Trim() ?? string.Empty;
            if (normalizedPayload.Length == 0)
            {
                StatusMessage = "No QR payload captured.";
                return;
            }

            if (!TryParseBootstrapPayload(normalizedPayload, out SyncBootstrapPayload? payload)
                || payload is null)
            {
                throw new InvalidOperationException("The scanned QR payload is invalid.");
            }

            SetPendingQrPairing(payload);
            StatusMessage = "QR scanned. Confirm pairing to connect to the desktop.";
            ErrorMessage = null;
        }, "QR scan failed");
    }

    private async Task ConfirmQrPairingAsync()
    {
        if (_pendingQrPairing is null)
        {
            return;
        }

        PendingQrPairingRequest pairingRequest = _pendingQrPairing;
        await RunOperationAsync(async () =>
        {
            await PairWithQrAsync(pairingRequest);
        }, "QR pairing failed");
    }

    private void CancelPendingQrPairing()
    {
        ClearPendingQrPairing();
        StatusMessage = "Pairing confirmation canceled. Scan the desktop QR to try again.";
        ErrorMessage = null;
    }

    private void RefreshDesktopQr()
    {
        if (!CanRefreshDesktopQr())
        {
            return;
        }

        string nextCode = _ingestionSyncApiHost?.RefreshPairCode() ?? AppGlobalSettings.GenerateSyncPairCode();
        PairCode = nextCode;

        if (_ingestionSyncApiHost is null)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            _appGlobalSettings.SyncApiPairCode = nextCode;
            _appGlobalSettings.SyncApiPairCodeIssuedAtUtc = now.ToString("O");
        }

        ErrorMessage = null;
        StatusMessage = BootstrapQrImageExportPath is null
            ? "Desktop QR refreshed."
            : $"Desktop QR refreshed and exported to {BootstrapQrImageExportPath}.";
    }

    private async Task TestConnectionAsync()
    {
        await RunOperationAsync(async () =>
        {
            await ConnectAndRefreshAsync();
        }, "Desktop sync connection failed");
    }

    private async Task PairWithQrAsync(PendingQrPairingRequest pairingRequest)
    {
        DesktopSyncPairClaimResponse claim = await _desktopSyncApiClient.ClaimPairTokenAsync(
            pairingRequest.ApiBaseUrl,
            pairingRequest.PairCode,
            DeviceLabel);
        if (!claim.Paired || string.IsNullOrWhiteSpace(claim.Token))
        {
            throw new InvalidOperationException("Pairing did not return a usable token.");
        }

        SyncToken = claim.Token;
        DesktopApiBaseUrl = ShouldAdoptPairApiBaseUrl(pairingRequest.ApiBaseUrl, claim.ApiBaseUrl)
            ? claim.ApiBaseUrl
            : pairingRequest.ApiBaseUrl;

        await ConnectAndRefreshAsync();
        ClearPendingQrPairing();
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
        _appGlobalSettings.SyncApiDeviceLabel = DeviceLabel;

        StatusMessage = $"Connected to {version.Api} v{version.Version}.";
        ErrorMessage = null;

        Diagnostics = new ConnectionDiagnostics
        {
            LastAttemptUtc = DateTime.UtcNow,
            ServerInfo = $"{version.Api} v{version.Version}",
            LastErrorMessage = null,
            LastErrorCategory = null,
            RemediationHint = null
        };

        await RefreshQueueCoreAsync();
        await RefreshLocalFilesCoreAsync();
    }

    private async Task RefreshQueueAsync()
    {
        await RunOperationAsync(RefreshQueueCoreAsync, "Failed to refresh ingestion queue");
    }

    private async Task RefreshLocalFilesAsync()
    {
        await RefreshLocalFilesSnapshotAsync();
    }

    public async Task RefreshLocalFilesSnapshotAsync()
    {
        await RunOperationAsync(
            RefreshLocalFilesCoreAsync,
            "Failed to refresh local files",
            disconnectOnFailure: false,
            failureStatusMessage: "Failed to refresh local files.");
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

    private async Task RefreshLocalFilesCoreAsync()
    {
        IReadOnlyList<WatcherFile> files = await _localDownloadFileCatalogService.GetFilesAsync(_appGlobalSettings.DownloadDir);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            LocalFiles.Clear();
            foreach (WatcherFile file in files)
            {
                LocalFiles.Add(file);
            }

            OnPropertyChanged(nameof(HasLocalFiles));
            OnPropertyChanged(nameof(HasSelectedLocalFiles));
            OnPropertyChanged(nameof(ShowNoLocalFilesHint));
            PushSelectedLocalFilesCommand.NotifyCanExecuteChanged();
        }, DispatcherPriority.Background);
    }

    private void OnLocalFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WatcherFile.Checked))
        {
            OnPropertyChanged(nameof(HasSelectedLocalFiles));
            PushSelectedLocalFilesCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task PushSelectedLocalFilesAsync()
    {
        await RunOperationAsync(async () =>
        {
            var selectedFiles = LocalFiles.Where(file => file.Checked).ToList();
            if (selectedFiles.Count == 0)
            {
                return;
            }

            int successCount = 0;
            List<string> failures = [];
            foreach (WatcherFile selectedFile in selectedFiles)
            {
                try
                {
                    _ = await _localIngestionPushService.PushAsync(
                        DesktopApiBaseUrl,
                        SyncToken,
                        new LocalIngestionEntry
                        {
                            LocalPath = selectedFile.FilePath,
                            DisplayName = selectedFile.DisplayName,
                        });

                    successCount++;
                    selectedFile.Checked = false;
                }
                catch (Exception ex)
                {
                    failures.Add($"{selectedFile.DisplayName}: {ex.Message}");
                }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException($"Pushed {successCount}/{selectedFiles.Count} file(s). Failures: {string.Join("; ", failures)}");
            }

            StatusMessage = $"Pushed {successCount} file(s) to desktop.";
            ErrorMessage = null;

            await RefreshQueueCoreAsync();
            await RefreshLocalFilesCoreAsync();
        }, "Failed to push selected local files");
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
                await RefreshLocalFilesCoreAsync();
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
        }, "Failed to trigger retry", disconnectOnFailure: false, failureStatusMessage: "Retry request failed.");
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
        }, "Failed to trigger install", disconnectOnFailure: false, failureStatusMessage: "Install request failed.");
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
        }, "Failed to request folder open", disconnectOnFailure: false, failureStatusMessage: "Open folder request failed.");
    }

    private async Task PushSelectedAsync(IngestionQueueItem? item)
    {
        if (item is null)
        {
            return;
        }

        DateTimeOffset initiatedAt = DateTimeOffset.UtcNow;
        item.LastActionResult = new ActionResult
        {
            ActionType = ActionType.Push,
            Status = ActionResultStatus.Pending,
            Message = "Processing...",
            InitiatedAtUtc = initiatedAt,
            CompletedAtUtc = initiatedAt
        };

        await RunOperationAsync(async () =>
        {
            try
            {
                long ingestionId = await _localIngestionPushService.PushAsync(
                    DesktopApiBaseUrl,
                    SyncToken,
                    new LocalIngestionEntry
                    {
                        LocalPath = item.DownloadedLocation ?? string.Empty,
                        DisplayName = item.DisplayName,
                        Source = item.Source,
                        SourceId = item.SourceId,
                        SourceLink = item.SourceLink,
                        Artist = item.Artist,
                        Title = item.Title,
                        Charter = item.Charter,
                        LibrarySource = item.LibrarySource,
                    });

                item.LastActionResult = new ActionResult
                {
                    ActionType = ActionType.Push,
                    Status = ActionResultStatus.Success,
                    Message = $"Pushed to desktop ingestion {ingestionId}",
                    InitiatedAtUtc = initiatedAt,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                };
                StatusMessage = $"Push requested for {item.DisplayName}.";
                ErrorMessage = null;
                await RefreshQueueCoreAsync();
            }
            catch (Exception ex)
            {
                item.LastActionResult = new ActionResult
                {
                    ActionType = ActionType.Push,
                    Status = ActionResultStatus.Failed,
                    Message = ex.Message,
                    InitiatedAtUtc = initiatedAt,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                };
                throw;
            }
        }, "Failed to push ingestion to desktop", disconnectOnFailure: false, failureStatusMessage: "Push request failed.");
    }

    private static bool ShouldAdoptPairApiBaseUrl(string currentBaseUrl, string? claimedBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(claimedBaseUrl)
            || !Uri.TryCreate(claimedBaseUrl.Trim(), UriKind.Absolute, out Uri? claimedUri))
        {
            return false;
        }

        if (!Uri.TryCreate(currentBaseUrl?.Trim(), UriKind.Absolute, out Uri? currentUri))
        {
            return true;
        }

        bool claimedLoopback = IsLoopbackHost(claimedUri);
        bool currentLoopback = IsLoopbackHost(currentUri);
        if (claimedLoopback && !currentLoopback)
        {
            return false;
        }

        return true;
    }

    private static bool IsLoopbackHost(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out IPAddress? ipAddress) && IPAddress.IsLoopback(ipAddress);
    }

    private void RegenerateBootstrapQrPayload()
    {
        string payload = BuildBootstrapPayload();
        GeneratedBootstrapPayload = payload;
        if (string.IsNullOrWhiteSpace(payload))
        {
            BootstrapQrImage = null;
            BootstrapQrImageExportPath = null;
            return;
        }

        string? exportedPath = null;
        try
        {
            using var generator = new QRCodeGenerator();
            using QRCodeData data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(data);
            byte[] pngBytes = qrCode.GetGraphic(12, drawQuietZones: true);
            exportedPath = _desktopSyncQrImageExportService.ExportDesktopQrImage(pngBytes);

            using var stream = new MemoryStream(pngBytes);
            BootstrapQrImage = new Bitmap(stream);
            BootstrapQrImageExportPath = exportedPath;
        }
        catch
        {
            BootstrapQrImage = null;
            BootstrapQrImageExportPath = exportedPath;
        }
    }

    private string BuildBootstrapPayload()
    {
        string pairCode = PairCode?.Trim() ?? string.Empty;
        string resolvedApiBaseUrl = ResolveBootstrapApiBaseUrl();
        if (string.IsNullOrWhiteSpace(pairCode)
            || string.IsNullOrWhiteSpace(resolvedApiBaseUrl))
        {
            return string.Empty;
        }

        var payload = new SyncBootstrapPayload(
            ApiBaseUrl: resolvedApiBaseUrl,
            PairCode: pairCode,
            DeviceLabel: DefaultDesktopBootstrapLabel,
            Version: 1);

        string json = JsonSerializer.Serialize(payload);
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return $"charthub-sync://bootstrap?d={encoded}";
    }

    private string ResolveBootstrapApiBaseUrl()
    {
        if (Uri.TryCreate(DesktopApiBaseUrl?.Trim(), UriKind.Absolute, out Uri? baseUri)
            && IsLanReachableBootstrapUri(baseUri))
        {
            return baseUri.GetLeftPart(UriPartial.Authority);
        }

        string resolvedFromHostSettings = SyncApiAddressResolver.ResolveAdvertisedApiBaseUrl(
            DefaultHostListenPrefix,
            string.Empty);
        if (Uri.TryCreate(resolvedFromHostSettings, UriKind.Absolute, out Uri? resolvedUri)
            && IsLanReachableBootstrapUri(resolvedUri))
        {
            return resolvedUri.GetLeftPart(UriPartial.Authority);
        }

        return string.Empty;
    }

    private static bool TryParseBootstrapPayload(string payloadText, out SyncBootstrapPayload? payload)
    {
        payload = null;

        string candidate = payloadText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (candidate.StartsWith('{'))
        {
            payload = TryDeserializePayload(candidate);
            return payload is not null;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (!uri.Scheme.Equals("charthub-sync", StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("bootstrap", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? encodedPayload = ParseQueryValue(uri.Query, "d") ?? ParseQueryValue(uri.Query, "data");
        if (string.IsNullOrWhiteSpace(encodedPayload))
        {
            return false;
        }

        try
        {
            string padded = encodedPayload
                .Replace('-', '+')
                .Replace('_', '/');
            int mod4 = padded.Length % 4;
            if (mod4 > 0)
            {
                padded = padded.PadRight(padded.Length + (4 - mod4), '=');
            }

            string json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            payload = TryDeserializePayload(json);
            return payload is not null;
        }
        catch
        {
            return false;
        }
    }

    private static SyncBootstrapPayload? TryDeserializePayload(string json)
    {
        try
        {
            SyncBootstrapPayload? payload = JsonSerializer.Deserialize<SyncBootstrapPayload>(json, SyncJsonOptions);
            if (payload is null
                || string.IsNullOrWhiteSpace(payload.ApiBaseUrl)
                || string.IsNullOrWhiteSpace(payload.PairCode)
                || !Uri.TryCreate(payload.ApiBaseUrl.Trim(), UriKind.Absolute, out Uri? parsed)
                || !IsLanReachableBootstrapUri(parsed))
            {
                return null;
            }

            return payload with
            {
                ApiBaseUrl = parsed.GetLeftPart(UriPartial.Authority),
                PairCode = payload.PairCode.Trim(),
                DeviceLabel = payload.DeviceLabel?.Trim() ?? string.Empty,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseQueryValue(string query, string key)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        string trimmed = query.TrimStart('?');
        foreach (string segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = segment.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            string currentKey = Uri.UnescapeDataString(parts[0]);
            if (!currentKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.UnescapeDataString(parts[1]);
        }

        return null;
    }

    private void SetPendingQrPairing(SyncBootstrapPayload payload)
    {
        _pendingQrPairing = new PendingQrPairingRequest(payload.ApiBaseUrl, payload.PairCode, payload.DeviceLabel);
        OnPropertyChanged(nameof(HasPendingQrPairing));
        OnPropertyChanged(nameof(PendingQrPairingSummary));
        OnPropertyChanged(nameof(ShowCompanionConfirmationSection));
        OnPropertyChanged(nameof(ShowCompanionScanSection));
        ConfirmQrPairingCommand.NotifyCanExecuteChanged();
        CancelPendingQrPairingCommand.NotifyCanExecuteChanged();
    }

    private void ClearPendingQrPairing()
    {
        if (_pendingQrPairing is null)
        {
            return;
        }

        _pendingQrPairing = null;
        OnPropertyChanged(nameof(HasPendingQrPairing));
        OnPropertyChanged(nameof(PendingQrPairingSummary));
        OnPropertyChanged(nameof(ShowCompanionConfirmationSection));
        OnPropertyChanged(nameof(ShowCompanionScanSection));
        ConfirmQrPairingCommand.NotifyCanExecuteChanged();
        CancelPendingQrPairingCommand.NotifyCanExecuteChanged();
    }

    private async Task RunOperationAsync(
        Func<Task> operation,
        string context,
        bool disconnectOnFailure = true,
        string? failureStatusMessage = null)
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
            if (disconnectOnFailure)
            {
                IsConnected = false;
                QueueItems.Clear();
                _lastAutoRefreshUtc = null;
                OnPropertyChanged(nameof(AutoRefreshHint));
            }

            ErrorMessage = ex.Message;
            StatusMessage = failureStatusMessage
                ?? (IsCompanionMode
                    ? "Pairing failed. Scan the desktop QR and try again."
                    : "Connection failed. Verify desktop URL and credentials, then retry.");

            (ErrorCategory category, string? remediation) = ClassifyError(ex);
            Diagnostics = new ConnectionDiagnostics
            {
                LastAttemptUtc = DateTime.UtcNow,
                ServerInfo = disconnectOnFailure ? null : Diagnostics.ServerInfo,
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

        if (ex is OperationCanceledException || message.Contains("canceled") || message.Contains("cancelled"))
        {
            return (ErrorCategory.NetworkUnreachable, "The desktop request timed out or was canceled. Retry and keep the companion paired.");
        }

        if (message.Contains("not found") || message.Contains("unreachable") || message.Contains("connection refused")
            || message.Contains("timeout") || message.Contains("no such host")
            || message.Contains("connection failure") || message.Contains("failed to connect"))
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

    private void OnLocalFilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (WatcherFile file in e.OldItems)
            {
                file.PropertyChanged -= OnLocalFilePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (WatcherFile file in e.NewItems)
            {
                file.PropertyChanged += OnLocalFilePropertyChanged;
            }
        }

        OnPropertyChanged(nameof(HasLocalFiles));
        OnPropertyChanged(nameof(HasSelectedLocalFiles));
        PushSelectedLocalFilesCommand.NotifyCanExecuteChanged();
    }

    private void OnAppGlobalSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppGlobalSettings.SyncApiPairCode))
        {
            PairCode = _appGlobalSettings.SyncApiPairCode;
        }
        else if (e.PropertyName == nameof(AppGlobalSettings.SyncApiDeviceLabel))
        {
            DeviceLabel = _appGlobalSettings.SyncApiDeviceLabel;
        }
    }

    public void Dispose()
    {
        QueueItems.CollectionChanged -= OnQueueItemsCollectionChanged;
        LocalFiles.CollectionChanged -= OnLocalFilesCollectionChanged;
        _appGlobalSettings.PropertyChanged -= OnAppGlobalSettingsPropertyChanged;

        foreach (WatcherFile file in LocalFiles)
        {
            file.PropertyChanged -= OnLocalFilePropertyChanged;
        }

        StopAutoRefreshLoop();
        BootstrapQrImage = null;
        BootstrapQrImageExportPath = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class NoOpQrCodeScannerService : IQrCodeScannerService
    {
        public bool IsSupported => false;

        public Task<string?> ScanAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class NoOpLocalIngestionPushService : ILocalIngestionPushService
    {
        public Task<long> PushAsync(string baseUrl, string token, LocalIngestionEntry entry, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Local ingestion push service is not configured.");
        }
    }

    private static bool IsLanReachableBootstrapUri(Uri uri)
    {
        if (uri.IsLoopback
            || string.IsNullOrWhiteSpace(uri.Host)
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !(IPAddress.TryParse(uri.Host, out IPAddress? parsedAddress) && IPAddress.IsLoopback(parsedAddress));
    }
}

public sealed record SyncBootstrapPayload(
    string ApiBaseUrl,
    string PairCode,
    string DeviceLabel,
    int Version);

internal sealed record PendingQrPairingRequest(
    string ApiBaseUrl,
    string PairCode,
    string DesktopLabel);