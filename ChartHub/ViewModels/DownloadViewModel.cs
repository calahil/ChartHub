using CommunityToolkit.Mvvm.Input;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Services.Transfers;
using ChartHub.Strings;
using ChartHub.Utilities;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;

namespace ChartHub.ViewModels
{
    public class DownloadViewModel : INotifyPropertyChanged, IAsyncDisposable
    {
        private readonly AppGlobalSettings globalSettings;
        public bool IsCompanionMode => OperatingSystem.IsAndroid();
        public bool IsDesktopMode => !OperatingSystem.IsAndroid();

        public IResourceWatcher DownloadWatcher { get; set; }
        public GoogleDriveWatcher GoogleWatcher { get; set; }

        public ICommand CheckAllCommand { get; }
        public IAsyncRelayCommand InstallSongs { get; }
        public IAsyncRelayCommand UploadCloud { get; }
        public IAsyncRelayCommand DownloadCloudToLocal { get; }
        public IAsyncRelayCommand SyncCloudToLocal { get; }

        private string _cloudConnectionHint = string.Empty;
        public string CloudConnectionHint
        {
            get => _cloudConnectionHint;
            private set
            {
                if (_cloudConnectionHint == value)
                    return;

                _cloudConnectionHint = value;
                OnPropertyChanged(nameof(CloudConnectionHint));
                OnPropertyChanged(nameof(HasCloudConnectionHint));
            }
        }

        public bool HasCloudConnectionHint => !string.IsNullOrWhiteSpace(CloudConnectionHint);
        public bool IsCloudConnected => !string.IsNullOrWhiteSpace(_googleDrive.ChartHubFolderId);

        private bool _isAnyChecked;
        public bool IsAnyChecked
        {
            get => _isAnyChecked;
            set
            {
                if (_isAnyChecked != value)
                {
                    _isAnyChecked = value;
                    OnPropertyChanged(nameof(IsAnyChecked));
                }
            }
        }
        private bool _isAllChecked;
        public bool IsAllChecked
        {
            get => _isAllChecked;
            set
            {
                if (_isAllChecked != value)
                {
                    _isAllChecked = value;
                    OnPropertyChanged(nameof(IsAllChecked));
                    CheckAllItems(value);
                }
            }
        }

        private bool _isAnyCloudChecked;
        public bool IsAnyCloudChecked
        {
            get => _isAnyCloudChecked;
            set
            {
                if (_isAnyCloudChecked != value)
                {
                    _isAnyCloudChecked = value;
                    OnPropertyChanged(nameof(IsAnyCloudChecked));
                }
            }
        }

        private WatcherFile? _selectedFile;
        public WatcherFile? SelectedFile
        {
            get => _selectedFile;
            set
            {
                _selectedFile = value;
                OnPropertyChanged(nameof(SelectedFile));
            }
        }

        public ObservableCollection<WatcherFile> DownloadFiles { get; set; }
        public ObservableCollection<WatcherFile> GoogleFiles { get; set; }
        public ObservableCollection<IngestionQueueItem> IngestionQueue { get; }

        public IReadOnlyList<string> QueueStateFilters { get; } =
        [
            "All",
            nameof(IngestionState.Queued),
            nameof(IngestionState.ResolvingSource),
            nameof(IngestionState.Downloading),
            nameof(IngestionState.Downloaded),
            nameof(IngestionState.Staged),
            nameof(IngestionState.Converting),
            nameof(IngestionState.Converted),
            nameof(IngestionState.Installing),
            nameof(IngestionState.Installed),
            nameof(IngestionState.Failed),
            nameof(IngestionState.Cancelled),
        ];

        public IReadOnlyList<string> QueueSortOptions { get; } = ["Updated", "Source", "State", "Name"];

        private string _selectedQueueStateFilter = "All";
        public string SelectedQueueStateFilter
        {
            get => _selectedQueueStateFilter;
            set
            {
                if (_selectedQueueStateFilter == value)
                    return;

                _selectedQueueStateFilter = value;
                OnPropertyChanged(nameof(SelectedQueueStateFilter));
                ObserveBackgroundTask(RefreshIngestionQueueAsync(), "Queue state filter changed");
            }
        }

        private string _selectedQueueSort = "Updated";
        public string SelectedQueueSort
        {
            get => _selectedQueueSort;
            set
            {
                if (_selectedQueueSort == value)
                    return;

                _selectedQueueSort = value;
                OnPropertyChanged(nameof(SelectedQueueSort));
                ObserveBackgroundTask(RefreshIngestionQueueAsync(), "Queue sort changed");
            }
        }

        private bool _isQueueSortDescending = true;
        public bool IsQueueSortDescending
        {
            get => _isQueueSortDescending;
            set
            {
                if (_isQueueSortDescending == value)
                    return;

                _isQueueSortDescending = value;
                OnPropertyChanged(nameof(IsQueueSortDescending));
                ObserveBackgroundTask(RefreshIngestionQueueAsync(), "Queue sort direction changed");
            }
        }

        private DownloadPageStrings _pageStrings;
        public DownloadPageStrings PageStrings
        {
            get { return _pageStrings; }
            set
            {
                if (_pageStrings != value)
                {
                    _pageStrings = value;
                    OnPropertyChanged(nameof(PageStrings));
                }
            }
        }

        private IGoogleDriveClient _googleDrive;
        private readonly ITransferOrchestrator _transferOrchestrator;
        private readonly ISongInstallService _songInstallService;
        private readonly SongIngestionCatalogService _ingestionCatalog;
        private readonly CancellationTokenSource _queueRefreshCts = new();
        private Task? _queueRefreshTask;

        public DownloadViewModel(
            AppGlobalSettings settings,
            IGoogleDriveClient googleDrive,
            ITransferOrchestrator transferOrchestrator,
            ISongInstallService songInstallService,
            SongIngestionCatalogService ingestionCatalog)
        {
            globalSettings = settings;
            _googleDrive = googleDrive;
            _transferOrchestrator = transferOrchestrator;
            _songInstallService = songInstallService;
            _ingestionCatalog = ingestionCatalog;
            DownloadWatcher = OperatingSystem.IsAndroid()
                ? new SnapshotResourceWatcher(globalSettings.DownloadDir, WatcherType.File)
                : new ResourceWatcher(globalSettings.DownloadDir, WatcherType.File);
            GoogleWatcher = new GoogleDriveWatcher(_googleDrive);
            CheckAllCommand = new RelayCommand(CheckAllItemsCommand);
            InstallSongs = new AsyncRelayCommand(InstallSongsCommand);
            UploadCloud = new AsyncRelayCommand(UploadCloudCommand, CanUploadCloud);
            DownloadCloudToLocal = new AsyncRelayCommand(DownloadCloudToLocalCommand, CanDownloadCloudToLocal);
            SyncCloudToLocal = new AsyncRelayCommand(SyncCloudToLocalCommand, CanSyncCloudToLocal);
            DownloadFiles = DownloadWatcher.Data;
            GoogleFiles = GoogleWatcher.Data;
            IngestionQueue = [];
            _pageStrings = new DownloadPageStrings();

            DownloadFiles.CollectionChanged += DownloadFiles_CollectionChanged;
            GoogleFiles.CollectionChanged += GoogleFiles_CollectionChanged;
            IngestionQueue.CollectionChanged += IngestionQueue_CollectionChanged;
            WireExistingWatcherItems();
            RefreshCloudConnectionState();
            ObserveBackgroundTask(RefreshIngestionQueueAsync(), "Initial ingestion queue load");
            ObserveBackgroundTask(ReconcileWatcherDataAsync(_queueRefreshCts.Token), "Initial watcher reconciliation");
            _queueRefreshTask = RunQueueRefreshLoopAsync(_queueRefreshCts.Token);
        }

        private void DownloadFiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems is not null)
            {
                foreach (WatcherFile file in e.NewItems)
                {
                    file.PropertyChanged += ItemPropertyChanged;
                    ObserveBackgroundTask(ReconcileLocalFileAsync(file, _queueRefreshCts.Token), "Local watcher reconciliation");
                }
            }

            if (e.OldItems is not null)
            {
                foreach (WatcherFile file in e.OldItems)
                    file.PropertyChanged -= ItemPropertyChanged;
            }

            ObserveBackgroundTask(RefreshIngestionQueueAsync(_queueRefreshCts.Token), "Queue refresh after local watcher change");
        }

        private void ItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is WatcherFile && e.PropertyName == nameof(WatcherFile.Checked))
            {
                IsAnyChecked = AnyItemChecked();
                IsAnyCloudChecked = AnyCloudItemChecked();
                NotifyCloudCommandStateChanged();
            }

            if (sender is IngestionQueueItem && e.PropertyName == nameof(IngestionQueueItem.Checked))
            {
                IsAnyChecked = AnyItemChecked();
                NotifyCloudCommandStateChanged();
            }
        }

        private void GoogleFiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems is not null)
            {
                foreach (WatcherFile file in e.NewItems)
                {
                    file.PropertyChanged += ItemPropertyChanged;
                    ObserveBackgroundTask(ReconcileCloudFileAsync(file, _queueRefreshCts.Token), "Cloud watcher reconciliation");
                }
            }

            if (e.OldItems is not null)
            {
                foreach (WatcherFile file in e.OldItems)
                    file.PropertyChanged -= ItemPropertyChanged;
            }

            ObserveBackgroundTask(RefreshIngestionQueueAsync(_queueRefreshCts.Token), "Queue refresh after cloud watcher change");
        }

        private void IngestionQueue_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems is not null)
            {
                foreach (IngestionQueueItem item in e.NewItems)
                    item.PropertyChanged += ItemPropertyChanged;
            }

            if (e.OldItems is not null)
            {
                foreach (IngestionQueueItem item in e.OldItems)
                    item.PropertyChanged -= ItemPropertyChanged;
            }
        }

        private void CheckAllItemsCommand()
        {
            IsAllChecked = !IsAllChecked;
        }

        public async Task UploadCloudCommand()
        {
            List<string> items = [];
            if (!EnsureCloudConnected())
                return;

            CloudConnectionHint = string.Empty;

            foreach (WatcherFile file in DownloadFiles)
            {
                if (file.Checked)
                {
                    try
                    {
                        if (!File.Exists(file.FilePath))
                        {
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Transfer", "Local file could not be inspected before upload", new Dictionary<string, object?>
                        {
                            ["filePath"] = file.FilePath,
                            ["reason"] = ex.GetType().Name,
                        });
                        Logger.LogError("Transfer", "Local file inspection failed before upload", ex, new Dictionary<string, object?>
                        {
                            ["filePath"] = file.FilePath,
                        });
                        continue;
                    }
                    try
                    {
                        await _googleDrive.UploadFileAsync(_googleDrive.ChartHubFolderId, file.FilePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Transfer", "Failed to upload local file to Google Drive", ex, new Dictionary<string, object?>
                        {
                            ["filePath"] = file.FilePath,
                            ["folderId"] = _googleDrive.ChartHubFolderId,
                        });
                    }
                }

            }
        }

        public async Task DownloadCloudToLocalCommand()
        {
            if (!EnsureCloudConnected())
                return;

            var selected = GoogleFiles.Where(file => file.Checked).ToList();
            if (selected.Count == 0)
                return;

            await _transferOrchestrator.DownloadSelectedCloudFilesToLocalAsync(selected);
            DownloadWatcher.LoadItems();
            await RefreshIngestionQueueAsync();
        }

        public async Task SyncCloudToLocalCommand()
        {
            if (!EnsureCloudConnected())
                return;

            await _transferOrchestrator.SyncCloudToLocalAdditiveAsync(GoogleFiles);
            DownloadWatcher.LoadItems();
            await RefreshIngestionQueueAsync();
        }

        private bool CanUploadCloud() => IsCloudConnected && IsAnyChecked;

        private bool CanDownloadCloudToLocal() => IsCloudConnected && IsAnyCloudChecked;

        private bool CanSyncCloudToLocal() => IsCloudConnected;

        public async Task HandleCloudAccountStateChangedAsync(bool isLinked, CancellationToken cancellationToken = default)
        {
            if (isLinked)
            {
                await GoogleWatcher.StartAsync(cancellationToken);
                GoogleWatcher.LoadItems();
            }
            else
            {
                GoogleFiles.Clear();
            }

            RefreshCloudConnectionState();
            await RefreshIngestionQueueAsync(cancellationToken);
        }

        private bool EnsureCloudConnected()
        {
            RefreshCloudConnectionState();
            return IsCloudConnected;
        }

        private void RefreshCloudConnectionState()
        {
            CloudConnectionHint = IsCloudConnected
                ? string.Empty
                : "Google Drive is not linked. Open Settings and link your Google account.";
            OnPropertyChanged(nameof(IsCloudConnected));
            NotifyCloudCommandStateChanged();
        }

        private void NotifyCloudCommandStateChanged()
        {
            UploadCloud.NotifyCanExecuteChanged();
            DownloadCloudToLocal.NotifyCanExecuteChanged();
            SyncCloudToLocal.NotifyCanExecuteChanged();
        }

        public async Task InstallSongsCommand()
        {
            var selectedQueuePaths = IngestionQueue
                .Where(item => item.Checked && item.CanInstall && !string.IsNullOrWhiteSpace(item.DownloadedLocation))
                .Select(item => item.DownloadedLocation!)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var selectedWatcherPaths = DownloadFiles
                .Where(file => file.Checked)
                .Select(file => file.FilePath)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var selectedPaths = selectedQueuePaths.Count > 0
                ? selectedQueuePaths
                : selectedWatcherPaths;

            if (selectedPaths.Count == 0)
                return;

            await _songInstallService.InstallSelectedDownloadsAsync(selectedPaths);

            foreach (var item in DownloadFiles.Where(file => file.Checked))
                item.Checked = false;

            foreach (var queueItem in IngestionQueue.Where(item => item.Checked))
                queueItem.Checked = false;

            DownloadWatcher.LoadItems();
            await RefreshIngestionQueueAsync();
        }

        public void CheckAllItems(bool isChecked)
        {
            foreach (var item in DownloadFiles)
            {
                item.Checked = isChecked;
            }
            OnPropertyChanged(nameof(DownloadFiles)); // Notify the UI to update
        }

        public bool AnyItemChecked()
        {
            return DownloadFiles.Any(item => item.Checked)
                || IngestionQueue.Any(item => item.Checked);
        }

        public bool AnyCloudItemChecked()
        {
            return GoogleFiles.Any(item => item.Checked);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async ValueTask DisposeAsync()
        {
            DownloadFiles.CollectionChanged -= DownloadFiles_CollectionChanged;
            GoogleFiles.CollectionChanged -= GoogleFiles_CollectionChanged;
            IngestionQueue.CollectionChanged -= IngestionQueue_CollectionChanged;

            foreach (var file in DownloadFiles)
                file.PropertyChanged -= ItemPropertyChanged;

            foreach (var file in GoogleFiles)
                file.PropertyChanged -= ItemPropertyChanged;

            foreach (var item in IngestionQueue)
                item.PropertyChanged -= ItemPropertyChanged;

            await _queueRefreshCts.CancelAsync();
            if (_queueRefreshTask is not null)
            {
                try
                {
                    await _queueRefreshTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
            _queueRefreshCts.Dispose();

            if (DownloadWatcher is IDisposable disposableWatcher)
                disposableWatcher.Dispose();

            await GoogleWatcher.DisposeAsync();
        }

        private async Task RefreshIngestionQueueAsync(CancellationToken cancellationToken = default)
        {
            var items = await _ingestionCatalog.QueryQueueAsync(
                stateFilter: SelectedQueueStateFilter,
                sourceFilter: null,
                sortBy: SelectedQueueSort,
                descending: IsQueueSortDescending,
                cancellationToken: cancellationToken);

            var selectedIds = IngestionQueue
                .Where(item => item.Checked)
                .Select(item => item.IngestionId)
                .ToHashSet();

            IngestionQueue.Clear();
            foreach (var item in items)
            {
                item.Checked = selectedIds.Contains(item.IngestionId);
                item.PropertyChanged += ItemPropertyChanged;
                IngestionQueue.Add(item);
            }

            IsAnyChecked = AnyItemChecked();
        }

        private static void ObserveBackgroundTask(Task task, string context)
        {
            _ = task.ContinueWith(t =>
            {
                var ex = t.Exception?.GetBaseException();
                if (ex is not null)
                {
                    Logger.LogError("Download", $"{context} failed", ex);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void WireExistingWatcherItems()
        {
            foreach (var file in DownloadFiles)
                file.PropertyChanged += ItemPropertyChanged;

            foreach (var file in GoogleFiles)
                file.PropertyChanged += ItemPropertyChanged;
        }

        private async Task ReconcileWatcherDataAsync(CancellationToken cancellationToken)
        {
            foreach (var file in DownloadFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReconcileLocalFileAsync(file, cancellationToken);
            }

            foreach (var file in GoogleFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ReconcileCloudFileAsync(file, cancellationToken);
            }

            await RefreshIngestionQueueAsync(cancellationToken);
        }

        private async Task ReconcileLocalFileAsync(WatcherFile file, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(file.FilePath) || !File.Exists(file.FilePath))
                return;

            var existing = await _ingestionCatalog.GetLatestIngestionByAssetLocationAsync(file.FilePath, cancellationToken);
            if (existing is not null)
                return;

            var sourceLink = BuildLocalSourceLink(file.FilePath);
            var ingestion = await _ingestionCatalog.GetOrCreateIngestionAsync("local", null, sourceLink, cancellationToken);
            var attempt = await _ingestionCatalog.StartAttemptAsync(ingestion.Id, cancellationToken);

            var fromState = ingestion.CurrentState;
            if (fromState != IngestionState.Downloaded)
            {
                await _ingestionCatalog.RecordStateTransitionAsync(
                    ingestion.Id,
                    attempt.Id,
                    fromState,
                    IngestionState.Downloaded,
                    "Discovered from local watcher",
                    cancellationToken);
            }

            await _ingestionCatalog.UpsertAssetAsync(new SongIngestionAssetEntry(
                IngestionId: ingestion.Id,
                AttemptId: attempt.Id,
                AssetRole: IngestionAssetRole.Downloaded,
                Location: file.FilePath,
                SizeBytes: file.SizeBytes,
                ContentHash: null,
                RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
        }

        private async Task ReconcileCloudFileAsync(WatcherFile file, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(file.FilePath))
                return;

            var existing = await _ingestionCatalog.GetLatestIngestionByAssetLocationAsync(file.FilePath, cancellationToken);
            if (existing is not null)
                return;

            var sourceLink = $"gdrive://{file.FilePath}";
            var ingestion = await _ingestionCatalog.GetOrCreateIngestionAsync("googledrive", file.FilePath, sourceLink, cancellationToken);
            var attempt = await _ingestionCatalog.StartAttemptAsync(ingestion.Id, cancellationToken);

            var fromState = ingestion.CurrentState;
            if (fromState != IngestionState.Downloaded)
            {
                await _ingestionCatalog.RecordStateTransitionAsync(
                    ingestion.Id,
                    attempt.Id,
                    fromState,
                    IngestionState.Downloaded,
                    "Discovered from cloud watcher",
                    cancellationToken);
            }

            await _ingestionCatalog.UpsertAssetAsync(new SongIngestionAssetEntry(
                IngestionId: ingestion.Id,
                AttemptId: attempt.Id,
                AssetRole: IngestionAssetRole.Downloaded,
                Location: file.FilePath,
                SizeBytes: file.SizeBytes,
                ContentHash: null,
                RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
        }

        private async Task RunQueueRefreshLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
                    await RefreshIngestionQueueAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError("Download", "Queue refresh loop failed", ex);
                }
            }
        }

        private static string BuildLocalSourceLink(string filePath)
        {
            if (Uri.TryCreate(filePath, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Scheme))
                return uri.AbsoluteUri;

            return new Uri(filePath).AbsoluteUri;
        }

    }
}
