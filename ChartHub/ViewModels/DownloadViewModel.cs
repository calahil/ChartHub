using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Net;
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
    private readonly SharedDownloadQueue _sharedDownloadQueue;
    private readonly Func<Action, Task> _uiInvoke;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
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

    private readonly CloneHeroViewModel? _cloneHeroViewModel;
    private readonly CancellationTokenSource _queueRefreshCts = new();
    private readonly Task? _queueRefreshTask;
    private readonly Dictionary<long, Guid> _serverQueueJobIds = [];
    private readonly HashSet<Guid> _trackedInstallJobIds = [];
    private readonly Dictionary<Guid, string> _trackedInstallStages = [];
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
        IChartHubServerApiClient serverApiClient,
        SharedDownloadQueue sharedDownloadQueue,
        CloneHeroViewModel? cloneHeroViewModel = null,
        Func<Action, Task>? uiInvoke = null)
    {
        _globalSettings = settings;
        _serverApiClient = serverApiClient;
        _sharedDownloadQueue = sharedDownloadQueue;
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
        _queueRefreshTask = RunQueueStreamLoopAsync(_queueRefreshCts.Token);
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
        var selectedInstallItems = IngestionQueue
            .Where(item => item.Checked && item.CanInstall && _serverQueueJobIds.ContainsKey(item.IngestionId))
            .ToList();

        if (selectedInstallItems.Count == 0)
        {
            ResetInstallPanel();
            InstallStageText = "No selected downloads are installable. Select Downloaded items.";
            AppendInstallLog(InstallStage.Preparing, "Warning", InstallStageText);
            Logger.LogWarning("Install", "Install request ignored because no selected queue items are installable.");
            return;
        }

        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            ResetInstallPanel();
            InstallStageText = "Install requires a configured ChartHub.Server endpoint and token.";
            AppendInstallLog(InstallStage.Failed, "Error", InstallStageText);
            Logger.LogWarning("Install", "Install request ignored because ChartHub.Server connection is not configured.");
            return;
        }

        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();
        ResetInstallPanel();
        IsInstallActive = true;
        IsInstallProgressIndeterminate = true;
        InstallStageText = selectedInstallItems.Count == 1 ? "Starting install" : $"Starting install for {selectedInstallItems.Count} items";
        InstallCurrentItemName = selectedInstallItems.Count == 1 ? selectedInstallItems[0].DisplayName : $"{selectedInstallItems.Count} selected items";
        _cancelInstallCommand.NotifyCanExecuteChanged();

        _trackedInstallJobIds.Clear();
        _trackedInstallStages.Clear();

        try
        {
            int submittedCount = 0;
            for (int index = 0; index < selectedInstallItems.Count; index++)
            {
                _installCts.Token.ThrowIfCancellationRequested();
                IngestionQueueItem item = selectedInstallItems[index];

                if (!_serverQueueJobIds.TryGetValue(item.IngestionId, out Guid jobId))
                {
                    continue;
                }

                InstallCurrentItemName = item.DisplayName;
                InstallStageText = $"Requesting install for {item.DisplayName}";
                InstallProgressValue = Math.Clamp((double)index / selectedInstallItems.Count * 100, 0, 100);
                AppendInstallLog(InstallStage.MovingToCloneHero, "Request", $"Install requested for job {jobId:D} ({item.DisplayName}).");
                Logger.LogInfo("Install", "Submitting install request to ChartHub.Server", new Dictionary<string, object?>
                {
                    ["jobId"] = jobId,
                    ["ingestionId"] = item.IngestionId,
                    ["displayName"] = item.DisplayName,
                    ["source"] = item.Source,
                    ["sourceId"] = item.SourceId,
                    ["baseUrl"] = baseUrl,
                });

                ChartHubServerDownloadJobResponse response = await _serverApiClient
                    .RequestInstallDownloadJobAsync(baseUrl, bearerToken, jobId, _installCts.Token)
                    ;

                item.Checked = false;
                submittedCount++;
                _trackedInstallJobIds.Add(response.JobId);
                _trackedInstallStages[response.JobId] = response.Stage;
                AppendInstallLog(InstallStage.MovingToCloneHero, "Accepted", $"Server accepted install for job {response.JobId:D} ({response.Stage}).");
                Logger.LogInfo("Install", "ChartHub.Server accepted install request", new Dictionary<string, object?>
                {
                    ["jobId"] = response.JobId,
                    ["displayName"] = item.DisplayName,
                    ["serverStage"] = response.Stage,
                    ["serverProgress"] = response.ProgressPercent,
                    ["serverDownloadedPath"] = response.DownloadedPath,
                    ["serverStagedPath"] = response.StagedPath,
                    ["serverInstalledPath"] = response.InstalledPath,
                });
            }

            InstallSummaryText = submittedCount == 1
                ? "Submitted 1 install request successfully."
                : $"Submitted {submittedCount} install requests successfully.";
            if (_trackedInstallJobIds.Count > 0)
            {
                IsInstallProgressIndeterminate = false;
                InstallProgressValue = 0;
                InstallStageText = "Install requests submitted. Waiting for server stage updates...";
            }
            else
            {
                IsInstallActive = false;
                InstallStageText = "No install jobs were submitted.";
            }
        }
        catch (OperationCanceledException)
        {
            _trackedInstallJobIds.Clear();
            _trackedInstallStages.Clear();
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
            _trackedInstallJobIds.Clear();
            _trackedInstallStages.Clear();
            string errorDetail = BuildInstallFailureDetail(ex);
            Logger.LogError("Install", "Install songs command failed", ex, new Dictionary<string, object?>
            {
                ["currentItem"] = InstallCurrentItemName,
                ["detail"] = errorDetail,
            });
            ApplyInstallProgress(new InstallProgressUpdate(
                InstallStage.Failed,
                "Install failed",
                InstallProgressValue,
                InstallCurrentItemName,
                errorDetail));
            InstallSummaryText = "Install failed. Check the log for details.";
        }
        finally
        {
            if (_trackedInstallJobIds.Count == 0)
            {
                IsInstallActive = false;
            }

            _cancelInstallCommand.NotifyCanExecuteChanged();
            _dismissInstallPanelCommand.NotifyCanExecuteChanged();
            _installCts.Dispose();
            _installCts = null;

            await RefreshIngestionQueueAsync();

            if (_cloneHeroViewModel is not null)
            {
                await _cloneHeroViewModel.RefreshAsync();
            }
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
            await DeleteQueueItemAsync(item);
        }

        await RefreshIngestionQueueAsync();
        await _uiInvoke(() =>
        {
            IsAnyChecked = AnyItemChecked();
            _deleteSelectedDownloadsCommand.NotifyCanExecuteChanged();
        });
    }

    private async Task DeleteQueueItemAsync(IngestionQueueItem item)
    {
        if (_isServerQueueActive && _serverQueueJobIds.TryGetValue(item.IngestionId, out Guid jobId))
        {
            await RequestCancelServerJobAsync(jobId);
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
            await _serverApiClient.RequestCancelDownloadJobAsync(baseUrl, bearerToken, jobId);
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
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<IngestionQueueItem> items;
            IReadOnlyList<ChartHubServerDownloadJobResponse> allJobs = [];
            if (TryGetServerConnection(out string baseUrl, out string bearerToken))
            {
                try
                {
                    ServerQueueSnapshot snapshot = await QueryServerQueueAsync(baseUrl, bearerToken, cancellationToken).ConfigureAwait(false);
                    items = snapshot.VisibleItems;
                    allJobs = snapshot.AllJobs;
                    _isServerQueueActive = true;
                }
                catch (Exception ex) when (IsUnauthorizedServerError(ex))
                {
                    HandleUnauthorizedServerError("Download", "ChartHub.Server token rejected during queue refresh; clearing local token.", baseUrl);
                    items = [];
                    allJobs = [];
                }
            }
            else
            {
                items = [];
                allJobs = [];
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
                ApplySharedDownloadJobs(allJobs);
                UpdateInstallProgressFromServerJobs(allJobs);

                IngestionQueue.Clear();
                foreach (IngestionQueueItem item in items)
                {
                    item.Checked = selectedIds.Contains(item.IngestionId);
                    IngestionQueue.Add(item);
                }

                IsAnyChecked = AnyItemChecked();
            }).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void UpdateInstallProgressFromServerJobs(IReadOnlyList<ChartHubServerDownloadJobResponse> jobs)
    {
        if (_trackedInstallJobIds.Count == 0)
        {
            return;
        }

        var jobsById = jobs.ToDictionary(job => job.JobId);
        int terminalCount = 0;
        int failedCount = 0;
        double totalProgress = 0;
        string? currentItemName = null;

        foreach (Guid jobId in _trackedInstallJobIds)
        {
            if (!jobsById.TryGetValue(jobId, out ChartHubServerDownloadJobResponse? job))
            {
                continue;
            }

            string currentStage = job.Stage;
            if (!_trackedInstallStages.TryGetValue(jobId, out string? previousStage)
                || !string.Equals(previousStage, currentStage, StringComparison.OrdinalIgnoreCase))
            {
                _trackedInstallStages[jobId] = currentStage;
                AppendInstallLog(InstallStage.MovingToCloneHero, "Stage", $"Job {job.JobId:D} ({job.DisplayName}) -> {currentStage} ({Math.Clamp(job.ProgressPercent, 0, 100):0.#}%).");

                if (string.Equals(currentStage, "Failed", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(job.Error))
                {
                    AppendInstallLog(InstallStage.Failed, "Error", $"Job {job.JobId:D} failed: {job.Error}");
                }
            }

            totalProgress += Math.Clamp(job.ProgressPercent, 0, 100);

            if (!IsTerminalServerStage(currentStage) && string.IsNullOrWhiteSpace(currentItemName))
            {
                currentItemName = job.DisplayName;
            }

            if (IsTerminalServerStage(currentStage))
            {
                terminalCount++;
                if (string.Equals(currentStage, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    failedCount++;
                }
            }
        }

        int trackedCount = _trackedInstallJobIds.Count;
        if (trackedCount > 0)
        {
            IsInstallProgressIndeterminate = false;
            InstallProgressValue = Math.Clamp(totalProgress / trackedCount, 0, 100);
        }

        if (!string.IsNullOrWhiteSpace(currentItemName))
        {
            InstallCurrentItemName = currentItemName;
        }

        if (terminalCount >= trackedCount)
        {
            IsInstallActive = false;
            int successCount = trackedCount - failedCount;
            InstallStageText = failedCount > 0
                ? $"Install processing finished with failures ({successCount} succeeded, {failedCount} failed)."
                : "Install processing finished successfully.";
            InstallSummaryText = failedCount > 0
                ? $"Install completed with failures ({successCount} succeeded, {failedCount} failed)."
                : $"Installed {successCount} item{(successCount == 1 ? string.Empty : "s")} successfully.";

            _trackedInstallJobIds.Clear();
            _trackedInstallStages.Clear();
        }
        else
        {
            InstallStageText = $"Install processing... {terminalCount}/{trackedCount} complete.";
        }
    }

    private async Task<ServerQueueSnapshot> QueryServerQueueAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken)
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

        return new ServerQueueSnapshot(jobs, queue);
    }

    private void ApplySharedDownloadJobs(IReadOnlyList<ChartHubServerDownloadJobResponse> jobs)
    {
        if (jobs.Count == 0 || _sharedDownloadQueue.Downloads.Count == 0)
        {
            return;
        }

        var jobsById = jobs.ToDictionary(job => job.JobId);
        foreach (DownloadFile card in _sharedDownloadQueue.Downloads)
        {
            if (!Guid.TryParse(card.Url, out Guid jobId)
                || !jobsById.TryGetValue(jobId, out ChartHubServerDownloadJobResponse? job))
            {
                continue;
            }

            string previousStage = card.Status;
            card.Status = job.Stage;
            card.DownloadProgress = Math.Clamp(job.ProgressPercent, 0, 100);
            card.Finished = IsTerminalServerStage(job.Stage);
            card.ErrorMessage = job.Error;

            if (!string.Equals(previousStage, job.Stage, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogInfo("ServerDownload", "Shared download card stage updated", new Dictionary<string, object?>
                {
                    ["jobId"] = jobId,
                    ["source"] = job.Source,
                    ["displayName"] = job.DisplayName,
                    ["from"] = previousStage,
                    ["to"] = job.Stage,
                    ["progress"] = card.DownloadProgress,
                });
            }
        }
    }

    private static DesktopState ResolveDesktopState(ChartHubServerDownloadJobResponse job)
    {
        if (!string.IsNullOrWhiteSpace(job.InstalledPath)
            || string.Equals(job.Stage, "Installed", StringComparison.OrdinalIgnoreCase)
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
            "installed" => IngestionState.Installed,
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
        if (ex is ChartHubServerApiException apiException)
        {
            return apiException.StatusCode == HttpStatusCode.Unauthorized;
        }

        return ex.Message.Contains("401 Unauthorized", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildInstallFailureDetail(Exception ex)
    {
        if (ex is ChartHubServerApiException apiException)
        {
            string response = string.IsNullOrWhiteSpace(apiException.ResponseBody)
                ? "(empty response body)"
                : apiException.ResponseBody;
            return $"Server install request failed ({(int)apiException.StatusCode} {apiException.ReasonPhrase}). ErrorCode={apiException.ErrorCode ?? "none"}. Response={response}";
        }

        return ex.Message;
    }

    private void HandleUnauthorizedServerError(string logCategory, string message, string? baseUrl = null)
    {
        _isServerQueueActive = false;
        _serverQueueJobIds.Clear();

        if (string.IsNullOrWhiteSpace(_globalSettings.ServerApiAuthToken))
        {
            return;
        }

        _globalSettings.ServerApiAuthToken = string.Empty;

        Dictionary<string, object?>? metadata = null;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            metadata = new Dictionary<string, object?>
            {
                ["baseUrl"] = baseUrl,
            };
        }

        Logger.LogWarning(logCategory, message, metadata);
    }

    private static bool IsTerminalServerStage(string stage)
    {
        return string.Equals(stage, "Installed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stage, "Completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stage, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stage, "Cancelled", StringComparison.OrdinalIgnoreCase);
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

    private async Task RunQueueStreamLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                Logger.LogInfo("ServerDownload", "Connecting to ChartHub.Server job stream", new Dictionary<string, object?>
                {
                    ["baseUrl"] = baseUrl,
                });

                await foreach (IReadOnlyList<ChartHubServerDownloadProgressEvent> _ in _serverApiClient
                                   .StreamDownloadJobsAsync(baseUrl, bearerToken, cancellationToken)
                                   .WithCancellation(cancellationToken)
                                   .ConfigureAwait(false))
                {
                    await RefreshIngestionQueueAsync(cancellationToken).ConfigureAwait(false);
                }

                Logger.LogWarning("ServerDownload", "ChartHub.Server job stream disconnected; reconnecting.", new Dictionary<string, object?>
                {
                    ["baseUrl"] = baseUrl,
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (IsUnauthorizedServerError(ex))
            {
                HandleUnauthorizedServerError("ServerDownload", "ChartHub.Server stream authorization failed; clearing local token.");
            }
            catch (Exception ex)
            {
                Logger.LogError("ServerDownload", "ChartHub.Server job stream failed", ex);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    private sealed record ServerQueueSnapshot(
        IReadOnlyList<ChartHubServerDownloadJobResponse> AllJobs,
        IReadOnlyList<IngestionQueueItem> VisibleItems);

}
