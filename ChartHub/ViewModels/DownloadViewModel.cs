using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;

using Avalonia.Threading;

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
    private readonly IChartHubServerApiClient _serverApiClient;
    private readonly Func<Action, Task> _uiInvoke;
    public bool IsCompanionMode => OperatingSystem.IsAndroid();
    public bool IsDesktopMode => !OperatingSystem.IsAndroid();

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

    public ObservableCollection<IngestionQueueItem> IngestionQueue { get; }
    public bool HasIngestionQueueItems => IngestionQueue.Count > 0;
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
    private readonly CloneHeroViewModel? _cloneHeroViewModel;
    private readonly CancellationTokenSource _queueRefreshCts = new();
    private readonly Task? _queueRefreshTask;
    private readonly Dictionary<long, Guid> _serverQueueJobIds = [];
    private bool _isServerQueueActive;
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
        IChartHubServerApiClient serverApiClient,
        CloneHeroViewModel? cloneHeroViewModel = null,
        Func<Action, Task>? uiInvoke = null)
    {
        _globalSettings = settings;
        _serverApiClient = serverApiClient;
        _songInstallService = songInstallService;
        _cloneHeroViewModel = cloneHeroViewModel;
        _uiInvoke = uiInvoke ?? (async action => await Dispatcher.UIThread.InvokeAsync(action));
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
        IngestionQueue = [];
        _pageStrings = new DownloadPageStrings();
        _isInstallLogExpanded = _globalSettings.InstallLogExpanded;

        IngestionQueue.CollectionChanged += IngestionQueue_CollectionChanged;
        ObserveBackgroundTask(RefreshIngestionQueueAsync(), "Initial ingestion queue load");
        _queueRefreshTask = RunQueueRefreshLoopAsync(_queueRefreshCts.Token);
    }

    private void ItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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

        List<string> selectedPaths = selectedQueuePaths;

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
        foreach (IngestionQueueItem item in IngestionQueue)
        {
            item.Checked = isChecked;
        }
        OnPropertyChanged(nameof(IngestionQueue));
    }

    public bool AnyItemChecked()
    {
        return IngestionQueue.Any(item => item.Checked);
    }

    private bool CanDeleteSelectedDownloads()
    {
        return IngestionQueue.Any(item => item.Checked);
    }

    private async Task DeleteSelectedDownloadsAsync()
    {
        var selectedQueueItems = IngestionQueue
            .Where(item => item.Checked)
            .ToList();

        if (selectedQueueItems.Count == 0)
        {
            return;
        }

        foreach (IngestionQueueItem item in selectedQueueItems)
        {
            await DeleteQueueItemAsync(item).ConfigureAwait(false);
        }

        await RefreshIngestionQueueAsync().ConfigureAwait(false);
        IsAnyChecked = AnyItemChecked();
        _deleteSelectedDownloadsCommand.NotifyCanExecuteChanged();
    }

    private async Task DeleteQueueItemAsync(IngestionQueueItem item)
    {
        if (_isServerQueueActive && _serverQueueJobIds.TryGetValue(item.IngestionId, out Guid jobId))
        {
            await RequestCancelServerJobAsync(jobId).ConfigureAwait(false);
            return;
        }

        Logger.LogWarning("Download", "Cannot cancel queue item because no active ChartHub.Server connection is configured.", new Dictionary<string, object?>
        {
            ["ingestionId"] = item.IngestionId,
            ["source"] = item.Source,
            ["sourceId"] = item.SourceId,
        });
    }

    private async Task RequestCancelServerJobAsync(Guid jobId)
    {
        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            return;
        }

        try
        {
            await _serverApiClient.RequestCancelDownloadJobAsync(baseUrl, bearerToken, jobId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError("Download", "Failed to cancel server download job", ex, new Dictionary<string, object?>
            {
                ["jobId"] = jobId,
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
        IngestionQueue.CollectionChanged -= IngestionQueue_CollectionChanged;

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
    }

    private async Task RefreshIngestionQueueAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IngestionQueueItem> items;
        if (TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            try
            {
                items = await QueryServerQueueAsync(baseUrl, bearerToken, cancellationToken).ConfigureAwait(false);
                _isServerQueueActive = true;
            }
            catch (Exception ex) when (IsUnauthorizedServerError(ex))
            {
                _isServerQueueActive = false;
                _serverQueueJobIds.Clear();
                _globalSettings.ServerApiAuthToken = string.Empty;
                Logger.LogWarning("Download", "ChartHub.Server token rejected during queue refresh; clearing local token.", new Dictionary<string, object?>
                {
                    ["baseUrl"] = baseUrl,
                });
                items = [];
            }
        }
        else
        {
            items = [];
            _isServerQueueActive = false;
            _serverQueueJobIds.Clear();
        }

        HashSet<long> selectedIds = [];
        await _uiInvoke(() =>
        {
            selectedIds = IngestionQueue
                .Where(item => item.Checked)
                .Select(item => item.IngestionId)
                .ToHashSet();
        }).ConfigureAwait(false);

        await _uiInvoke(() =>
        {
            IngestionQueue.Clear();
            foreach (IngestionQueueItem item in items)
            {
                item.Checked = selectedIds.Contains(item.IngestionId);
                IngestionQueue.Add(item);
            }

            IsAnyChecked = AnyItemChecked();
        }).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<IngestionQueueItem>> QueryServerQueueAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChartHubServerDownloadJobResponse> jobs = await _serverApiClient
            .ListDownloadJobsAsync(baseUrl, bearerToken, cancellationToken)
            .ConfigureAwait(false);

        IEnumerable<ChartHubServerDownloadJobResponse> filtered = jobs;
        if (!string.Equals(SelectedQueueStateFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(job => string.Equals(job.Stage, SelectedQueueStateFilter, StringComparison.OrdinalIgnoreCase));
        }

        filtered = SelectedQueueSort switch
        {
            "Source" => IsQueueSortDescending
                ? filtered.OrderByDescending(item => item.Source, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(item => item.Source, StringComparer.OrdinalIgnoreCase),
            "State" => IsQueueSortDescending
                ? filtered.OrderByDescending(item => item.Stage, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(item => item.Stage, StringComparer.OrdinalIgnoreCase),
            "Name" => IsQueueSortDescending
                ? filtered.OrderByDescending(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => IsQueueSortDescending
                ? filtered.OrderByDescending(item => item.UpdatedAtUtc)
                : filtered.OrderBy(item => item.UpdatedAtUtc),
        };

        _serverQueueJobIds.Clear();
        List<IngestionQueueItem> queue = [];
        foreach (ChartHubServerDownloadJobResponse job in filtered)
        {
            long ingestionId = BitConverter.ToInt64(job.JobId.ToByteArray(), 0);
            _serverQueueJobIds[ingestionId] = job.JobId;
            queue.Add(new IngestionQueueItem
            {
                IngestionId = ingestionId,
                Source = job.Source,
                SourceId = job.SourceId,
                SourceLink = job.SourceUrl,
                DisplayName = job.DisplayName,
                CurrentState = MapServerStage(job.Stage),
                DownloadedLocation = job.DownloadedPath,
                InstalledLocation = job.InstalledPath,
                DesktopState = ResolveDesktopState(job),
                UpdatedAtUtc = job.UpdatedAtUtc,
                LibrarySource = job.Source,
            });
        }

        return queue;
    }

    private static DesktopState ResolveDesktopState(ChartHubServerDownloadJobResponse job)
    {
        if (!string.IsNullOrWhiteSpace(job.InstalledPath)
            || string.Equals(job.Stage, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return DesktopState.Installed;
        }

        if (!string.IsNullOrWhiteSpace(job.DownloadedPath)
            || string.Equals(job.Stage, "Downloaded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(job.Stage, "Staged", StringComparison.OrdinalIgnoreCase))
        {
            return DesktopState.Downloaded;
        }

        return DesktopState.Cloud;
    }

    private static IngestionState MapServerStage(string stage)
    {
        if (Enum.TryParse(stage, ignoreCase: true, out IngestionState parsed))
        {
            return parsed;
        }

        return stage.ToLowerInvariant() switch
        {
            "completed" => IngestionState.Installed,
            "cancelling" => IngestionState.Downloading,
            _ => IngestionState.Queued,
        };
    }

    private bool TryGetServerConnection(out string baseUrl, out string bearerToken)
    {
        baseUrl = NormalizeApiBaseUrl(_globalSettings.ServerApiBaseUrl);
        bearerToken = _globalSettings.ServerApiAuthToken?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(bearerToken);
    }

    private static string NormalizeApiBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return string.Empty;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static bool IsUnauthorizedServerError(Exception ex)
    {
        return ex.Message.Contains("401 Unauthorized", StringComparison.OrdinalIgnoreCase);
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
