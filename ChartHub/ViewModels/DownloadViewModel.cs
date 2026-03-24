using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;

using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Services.Transfers;
using ChartHub.Strings;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

namespace ChartHub.ViewModels;

public class DownloadViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly AppGlobalSettings _globalSettings;
    public bool IsCompanionMode => OperatingSystem.IsAndroid();
    public bool IsDesktopMode => !OperatingSystem.IsAndroid();

    public IResourceWatcher DownloadWatcher { get; set; }

    public ICommand CheckAllCommand { get; }
    public IAsyncRelayCommand InstallSongs { get; }
    public IAsyncRelayCommand DeleteSelectedDownloadsCommand { get; }
    public ICommand CancelInstallCommand { get; }
    public ICommand ClearInstallLogCommand { get; }
    public ICommand ToggleInstallLogCommand { get; }
    public ICommand DismissInstallPanelCommand { get; }

    private bool _isInstallPanelVisible;
    public bool IsInstallPanelVisible
    {
        get => _isInstallPanelVisible;
        private set
        {
            if (_isInstallPanelVisible == value)
            {
                return;
            }

            _isInstallPanelVisible = value;
            OnPropertyChanged(nameof(IsInstallPanelVisible));
        }
    }

    public bool IsInstallIdle => !IsInstallActive;

    private bool _isInstallActive;
    public bool IsInstallActive
    {
        get => _isInstallActive;
        private set
        {
            if (_isInstallActive == value)
            {
                return;
            }

            _isInstallActive = value;
            OnPropertyChanged(nameof(IsInstallActive));
            OnPropertyChanged(nameof(IsInstallIdle));
            _dismissInstallPanelCommand?.NotifyCanExecuteChanged();
            UpdateInstallPanelVisibility();
        }
    }

    private bool _isInstallProgressIndeterminate;
    public bool IsInstallProgressIndeterminate
    {
        get => _isInstallProgressIndeterminate;
        private set
        {
            if (_isInstallProgressIndeterminate == value)
            {
                return;
            }

            _isInstallProgressIndeterminate = value;
            OnPropertyChanged(nameof(IsInstallProgressIndeterminate));
        }
    }

    private double _installProgressValue;
    public double InstallProgressValue
    {
        get => _installProgressValue;
        private set
        {
            if (Math.Abs(_installProgressValue - value) < 0.001)
            {
                return;
            }

            _installProgressValue = value;
            OnPropertyChanged(nameof(InstallProgressValue));
        }
    }

    private string _installStageText = string.Empty;
    public string InstallStageText
    {
        get => _installStageText;
        private set
        {
            if (_installStageText == value)
            {
                return;
            }

            _installStageText = value;
            OnPropertyChanged(nameof(InstallStageText));
        }
    }

    private string _installCurrentItemName = string.Empty;
    public string InstallCurrentItemName
    {
        get => _installCurrentItemName;
        private set
        {
            if (_installCurrentItemName == value)
            {
                return;
            }

            _installCurrentItemName = value;
            OnPropertyChanged(nameof(InstallCurrentItemName));
        }
    }

    public ObservableCollection<string> InstallLogItems { get; } = [];

    public bool CanCopyAllInstallLogs => InstallLogItems.Count > 0;

    private string _installSummaryText = string.Empty;
    public string InstallSummaryText
    {
        get => _installSummaryText;
        private set
        {
            if (_installSummaryText == value)
            {
                return;
            }

            _installSummaryText = value;
            OnPropertyChanged(nameof(InstallSummaryText));
            OnPropertyChanged(nameof(HasInstallSummary));
            _dismissInstallPanelCommand?.NotifyCanExecuteChanged();
            UpdateInstallPanelVisibility();
        }
    }

    public bool HasInstallSummary => !string.IsNullOrWhiteSpace(InstallSummaryText);

    private bool _isInstallLogExpanded = true;
    public bool IsInstallLogExpanded
    {
        get => _isInstallLogExpanded;
        private set
        {
            if (_isInstallLogExpanded == value)
            {
                return;
            }

            _isInstallLogExpanded = value;
            _globalSettings.InstallLogExpanded = value;
            OnPropertyChanged(nameof(IsInstallLogExpanded));
            OnPropertyChanged(nameof(InstallLogToggleText));
        }
    }

    public string InstallLogToggleText => IsInstallLogExpanded ? "Collapse Log" : "Expand Log";

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
    public ObservableCollection<IngestionQueueItem> IngestionQueue { get; }
    public bool HasLocalDownloadFiles => DownloadFiles.Count > 0;
    public bool HasIngestionQueueItems => IngestionQueue.Count > 0;
    public bool ShowLocalEmptyState => !HasLocalDownloadFiles;
    public bool ShowQueueEmptyState => !HasIngestionQueueItems;

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
            {
                return;
            }

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
            {
                return;
            }

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
            {
                return;
            }

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

    private readonly ISongInstallService _songInstallService;
    private readonly SongIngestionCatalogService _ingestionCatalog;
    private readonly LibraryCatalogService _libraryCatalog;
    private readonly ILocalFileDeletionService _localFileDeletionService;
    private readonly CloneHeroViewModel? _cloneHeroViewModel;
    private readonly CancellationTokenSource _queueRefreshCts = new();
    private readonly Task? _queueRefreshTask;
    private CancellationTokenSource? _installCts;
    private const int MaxInstallLogItems = 500;
    private readonly RelayCommand _cancelInstallCommand;
    private readonly RelayCommand _clearInstallLogCommand;
    private readonly RelayCommand _toggleInstallLogCommand;
    private readonly RelayCommand _dismissInstallPanelCommand;
    private readonly AsyncRelayCommand _deleteSelectedDownloadsCommand;

    public DownloadViewModel(
        AppGlobalSettings settings,
        ISongInstallService songInstallService,
        SongIngestionCatalogService ingestionCatalog,
        LibraryCatalogService libraryCatalog,
        ILocalFileDeletionService localFileDeletionService,
        CloneHeroViewModel? cloneHeroViewModel = null)
    {
        _globalSettings = settings;
        _songInstallService = songInstallService;
        _ingestionCatalog = ingestionCatalog;
        _libraryCatalog = libraryCatalog;
        _localFileDeletionService = localFileDeletionService;
        _cloneHeroViewModel = cloneHeroViewModel;
        DownloadWatcher = OperatingSystem.IsAndroid()
            ? new SnapshotResourceWatcher(_globalSettings.DownloadDir, WatcherType.File)
            : new ResourceWatcher(_globalSettings.DownloadDir, WatcherType.File);
        CheckAllCommand = new RelayCommand(CheckAllItemsCommand);
        InstallSongs = new AsyncRelayCommand(InstallSongsCommand);
        _deleteSelectedDownloadsCommand = new AsyncRelayCommand(DeleteSelectedDownloadsAsync, CanDeleteSelectedDownloads);
        DeleteSelectedDownloadsCommand = _deleteSelectedDownloadsCommand;
        _cancelInstallCommand = new RelayCommand(CancelInstall, CanCancelInstall);
        CancelInstallCommand = _cancelInstallCommand;
        _clearInstallLogCommand = new RelayCommand(ClearInstallLog, CanClearInstallLog);
        ClearInstallLogCommand = _clearInstallLogCommand;
        _toggleInstallLogCommand = new RelayCommand(ToggleInstallLog);
        ToggleInstallLogCommand = _toggleInstallLogCommand;
        _dismissInstallPanelCommand = new RelayCommand(DismissInstallPanel, CanDismissInstallPanel);
        DismissInstallPanelCommand = _dismissInstallPanelCommand;
        DownloadFiles = DownloadWatcher.Data;
        IngestionQueue = [];
        _pageStrings = new DownloadPageStrings();
        _isInstallLogExpanded = _globalSettings.InstallLogExpanded;

        DownloadFiles.CollectionChanged += DownloadFiles_CollectionChanged;
        IngestionQueue.CollectionChanged += IngestionQueue_CollectionChanged;
        WireExistingWatcherItems();
        DownloadWatcher.LoadItems();
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
            {
                file.PropertyChanged -= ItemPropertyChanged;
            }
        }

        _deleteSelectedDownloadsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasLocalDownloadFiles));
        OnPropertyChanged(nameof(ShowLocalEmptyState));
        ObserveBackgroundTask(RefreshIngestionQueueAsync(_queueRefreshCts.Token), "Queue refresh after local watcher change");
    }

    private void ItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is WatcherFile && e.PropertyName == nameof(WatcherFile.Checked))
        {
            IsAnyChecked = AnyItemChecked();
            _deleteSelectedDownloadsCommand.NotifyCanExecuteChanged();
        }

        if (sender is IngestionQueueItem && e.PropertyName == nameof(IngestionQueueItem.Checked))
        {
            IsAnyChecked = AnyItemChecked();
            _deleteSelectedDownloadsCommand.NotifyCanExecuteChanged();
        }
    }

    private void IngestionQueue_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (IngestionQueueItem item in e.NewItems)
            {
                item.PropertyChanged += ItemPropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (IngestionQueueItem item in e.OldItems)
            {
                item.PropertyChanged -= ItemPropertyChanged;
            }
        }

        _deleteSelectedDownloadsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasIngestionQueueItems));
        OnPropertyChanged(nameof(ShowQueueEmptyState));
    }

    private void CheckAllItemsCommand()
    {
        IsAllChecked = !IsAllChecked;
    }

    private bool CanCancelInstall() => IsInstallActive;

    private void CancelInstall()
    {
        _installCts?.Cancel();
    }

    private bool CanClearInstallLog() => InstallLogItems.Count > 0;

    private void ClearInstallLog()
    {
        InstallLogItems.Clear();
        _clearInstallLogCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCopyAllInstallLogs));
        UpdateInstallPanelVisibility();
    }

    private void ToggleInstallLog()
    {
        IsInstallLogExpanded = !IsInstallLogExpanded;
    }

    private bool CanDismissInstallPanel() => !IsInstallActive && (HasInstallSummary || InstallLogItems.Count > 0);

    private void DismissInstallPanel()
    {
        InstallLogItems.Clear();
        InstallSummaryText = string.Empty;
        InstallCurrentItemName = string.Empty;
        InstallStageText = string.Empty;
        InstallProgressValue = 0;
        IsInstallProgressIndeterminate = false;
        _clearInstallLogCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCopyAllInstallLogs));
        UpdateInstallPanelVisibility();
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

        List<string> selectedPaths = selectedQueuePaths.Count > 0
            ? selectedQueuePaths
            : selectedWatcherPaths;

        if (selectedPaths.Count == 0)
        {
            return;
        }

        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();
        ResetInstallPanel();
        IsInstallActive = true;
        InstallStageText = selectedPaths.Count == 1 ? "Starting install" : $"Starting install for {selectedPaths.Count} items";
        InstallCurrentItemName = selectedPaths.Count == 1 ? Path.GetFileName(selectedPaths[0]) : $"{selectedPaths.Count} selected items";
        _cancelInstallCommand.NotifyCanExecuteChanged();

        var progress = new Progress<InstallProgressUpdate>(ApplyInstallProgress);

        try
        {
            IReadOnlyList<string> installedPaths = await _songInstallService.InstallSelectedDownloadsAsync(selectedPaths, progress, _installCts.Token);
            int installedCount = installedPaths.Count;
            InstallSummaryText = installedCount == 1
                ? "Installed 1 item successfully."
                : $"Installed {installedCount} items successfully.";

            foreach (WatcherFile? item in DownloadFiles.Where(file => file.Checked))
            {
                item.Checked = false;
            }

            foreach (IngestionQueueItem? queueItem in IngestionQueue.Where(item => item.Checked))
            {
                queueItem.Checked = false;
            }

            // Refresh Clone Hero library to show newly installed songs
            if (_cloneHeroViewModel is not null)
            {
                await _cloneHeroViewModel.RefreshAsync();
            }
        }
        catch (OperationCanceledException)
        {
            ApplyInstallProgress(new InstallProgressUpdate(
                InstallStage.Cancelled,
                "Install cancelled",
                InstallProgressValue,
                InstallCurrentItemName,
                "Install cancelled by user."));
            InstallSummaryText = "Install cancelled.";
        }
        catch (Exception ex)
        {
            Logger.LogError("Install", "Install songs command failed", ex);
            ApplyInstallProgress(new InstallProgressUpdate(
                InstallStage.Failed,
                "Install failed",
                InstallProgressValue,
                InstallCurrentItemName,
                ex.Message));
            InstallSummaryText = "Install failed. Check the log for details.";
        }
        finally
        {
            IsInstallActive = false;
            _cancelInstallCommand.NotifyCanExecuteChanged();
            _dismissInstallPanelCommand.NotifyCanExecuteChanged();
            _installCts.Dispose();
            _installCts = null;

            DownloadWatcher.LoadItems();
            await RefreshIngestionQueueAsync();
        }
    }

    private void ResetInstallPanel()
    {
        InstallLogItems.Clear();
        InstallSummaryText = string.Empty;
        InstallProgressValue = 0;
        InstallStageText = string.Empty;
        InstallCurrentItemName = string.Empty;
        IsInstallProgressIndeterminate = false;
        _clearInstallLogCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCopyAllInstallLogs));
        UpdateInstallPanelVisibility();
    }

    private void UpdateInstallPanelVisibility()
    {
        IsInstallPanelVisible = IsInstallActive || HasInstallSummary || InstallLogItems.Count > 0;
    }

    private void ApplyInstallProgress(InstallProgressUpdate update)
    {
        InstallStageText = update.Message;
        if (!string.IsNullOrWhiteSpace(update.CurrentItemName))
        {
            InstallCurrentItemName = update.CurrentItemName;
        }

        if (update.ProgressPercent.HasValue)
        {
            InstallProgressValue = Math.Clamp(update.ProgressPercent.Value, 0, 100);
        }

        IsInstallProgressIndeterminate = update.IsIndeterminate;

        if (!string.IsNullOrWhiteSpace(update.Message))
        {
            AppendInstallLog(update.Stage, "Status", update.Message);
        }

        if (!string.IsNullOrWhiteSpace(update.LogLine))
        {
            AppendInstallLog(update.Stage, "Detail", update.LogLine);
        }
    }

    private void AppendInstallLog(InstallStage stage, string category, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string normalizedText = text.ReplaceLineEndings(" ").Trim();
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return;
        }

        string normalizedCategory = string.IsNullOrWhiteSpace(category) ? "Info" : category.Trim();
        string item = $"{stage} | {normalizedCategory} | {normalizedText}";

        InstallLogItems.Add(item);
        while (InstallLogItems.Count > MaxInstallLogItems)
        {
            InstallLogItems.RemoveAt(0);
        }

        _clearInstallLogCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCopyAllInstallLogs));
        UpdateInstallPanelVisibility();
    }

    public void CheckAllItems(bool isChecked)
    {
        foreach (WatcherFile item in DownloadFiles)
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

    private bool CanDeleteSelectedDownloads()
    {
        return DownloadFiles.Any(file => file.Checked)
            || IngestionQueue.Any(item => item.Checked);
    }

    private async Task DeleteSelectedDownloadsAsync()
    {
        var selectedQueueItems = IngestionQueue
            .Where(item => item.Checked)
            .ToList();

        var selectedWatcherFiles = DownloadFiles
            .Where(file => file.Checked)
            .ToList();

        if (selectedQueueItems.Count == 0 && selectedWatcherFiles.Count == 0)
        {
            return;
        }

        foreach (IngestionQueueItem item in selectedQueueItems)
        {
            await DeleteQueueItemAsync(item).ConfigureAwait(false);
        }

        foreach (WatcherFile file in selectedWatcherFiles)
        {
            await DeleteWatcherFileAsync(file).ConfigureAwait(false);
        }

        DownloadWatcher.LoadItems();
        await RefreshIngestionQueueAsync().ConfigureAwait(false);
        IsAnyChecked = AnyItemChecked();
        _deleteSelectedDownloadsCommand.NotifyCanExecuteChanged();
    }

    private async Task DeleteQueueItemAsync(IngestionQueueItem item)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(item.DownloadedLocation))
            {
                await _localFileDeletionService.DeletePathIfExistsAsync(item.DownloadedLocation).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(item.Source) && !string.IsNullOrWhiteSpace(item.SourceId))
            {
                await _libraryCatalog.RemoveAsync(item.Source, item.SourceId).ConfigureAwait(false);
            }

            await _ingestionCatalog.RemoveIngestionAsync(item.IngestionId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError("Download", "Failed to delete selected queue item", ex, new Dictionary<string, object?>
            {
                ["ingestionId"] = item.IngestionId,
                ["source"] = item.Source,
                ["sourceId"] = item.SourceId,
            });
        }
    }

    private async Task DeleteWatcherFileAsync(WatcherFile file)
    {
        try
        {
            await _localFileDeletionService.DeletePathIfExistsAsync(file.FilePath).ConfigureAwait(false);

            SongIngestionRecord? linkedIngestion = await _ingestionCatalog
                .GetLatestIngestionByAssetLocationAsync(file.FilePath)
                .ConfigureAwait(false);

            if (linkedIngestion is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(linkedIngestion.Source)
                && !string.IsNullOrWhiteSpace(linkedIngestion.SourceId))
            {
                await _libraryCatalog.RemoveAsync(linkedIngestion.Source, linkedIngestion.SourceId).ConfigureAwait(false);
            }

            await _ingestionCatalog.RemoveIngestionAsync(linkedIngestion.Id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError("Download", "Failed to delete selected local download", ex, new Dictionary<string, object?>
            {
                ["path"] = file.FilePath,
            });
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public async ValueTask DisposeAsync()
    {
        _installCts?.Cancel();
        _installCts?.Dispose();
        DownloadFiles.CollectionChanged -= DownloadFiles_CollectionChanged;
        IngestionQueue.CollectionChanged -= IngestionQueue_CollectionChanged;

        foreach (WatcherFile file in DownloadFiles)
        {
            file.PropertyChanged -= ItemPropertyChanged;
        }

        foreach (IngestionQueueItem item in IngestionQueue)
        {
            item.PropertyChanged -= ItemPropertyChanged;
        }

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
        {
            disposableWatcher.Dispose();
        }
    }

    private async Task RefreshIngestionQueueAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IngestionQueueItem> items = await _ingestionCatalog.QueryQueueAsync(
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
        foreach (IngestionQueueItem item in items)
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
            Exception? ex = t.Exception?.GetBaseException();
            if (ex is not null)
            {
                Logger.LogError("Download", $"{context} failed", ex);
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void WireExistingWatcherItems()
    {
        foreach (WatcherFile file in DownloadFiles)
        {
            file.PropertyChanged += ItemPropertyChanged;
        }
    }

    private async Task ReconcileWatcherDataAsync(CancellationToken cancellationToken)
    {
        foreach (WatcherFile file in DownloadFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReconcileLocalFileAsync(file, cancellationToken);
        }

        await RefreshIngestionQueueAsync(cancellationToken);
    }

    private async Task ReconcileLocalFileAsync(WatcherFile file, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(file.FilePath) || !File.Exists(file.FilePath))
        {
            return;
        }

        // Local watcher files are unmanaged unless they were already ingested through trusted sources.
        await Task.CompletedTask;
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

}
