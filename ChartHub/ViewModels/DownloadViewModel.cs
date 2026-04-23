using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;

using Avalonia.Threading;

using ChartHub.Localization;
using ChartHub.Models;
using ChartHub.Services;
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

    public System.Windows.Input.ICommand CheckAllCommand { get; }
    public IAsyncRelayCommand InstallSongs { get; }
    public IAsyncRelayCommand<IngestionQueueItem> DeleteJobCommand { get; }
    public IAsyncRelayCommand<IngestionQueueItem> ToggleJobLogCommand { get; }

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
    private readonly IStatusBarService _statusBarService;
    private readonly CancellationTokenSource _queueRefreshCts = new();
    private readonly Task? _queueRefreshTask;
    private readonly Dictionary<long, Guid> _serverQueueJobIds = [];

    public DownloadViewModel(
        AppGlobalSettings settings,
        IChartHubServerApiClient serverApiClient,
        SharedDownloadQueue sharedDownloadQueue,
        CloneHeroViewModel? cloneHeroViewModel = null,
        Func<Action, Task>? uiInvoke = null,
        IStatusBarService? statusBarService = null)
    {
        _globalSettings = settings;
        _serverApiClient = serverApiClient;
        _sharedDownloadQueue = sharedDownloadQueue;
        _cloneHeroViewModel = cloneHeroViewModel;
        _statusBarService = statusBarService ?? new StatusBarService();
        _uiInvoke = uiInvoke ?? (async action => await Dispatcher.UIThread.InvokeAsync(action));
        CheckAllCommand = new RelayCommand(CheckAllItemsCommand);
        InstallSongs = new AsyncRelayCommand(InstallSongsCommandAsync);
        DeleteJobCommand = new AsyncRelayCommand<IngestionQueueItem>(DeleteJobAsync);
        ToggleJobLogCommand = new AsyncRelayCommand<IngestionQueueItem>(ToggleJobLogAsync);
        IngestionQueue = [];
        _pageStrings = new DownloadPageStrings();

        IngestionQueue.CollectionChanged += IngestionQueue_CollectionChanged;
        ObserveBackgroundTask(RefreshIngestionQueueAsync(), "Initial ingestion queue load");
        _queueRefreshTask = RunQueueStreamLoopAsync(_queueRefreshCts.Token);
    }

    private void ItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is IngestionQueueItem && e.PropertyName == nameof(IngestionQueueItem.Checked))
        {
            IsAnyChecked = AnyItemChecked();
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

        OnPropertyChanged(nameof(HasIngestionQueueItems));
        OnPropertyChanged(nameof(ShowQueueEmptyState));
    }

    private void CheckAllItemsCommand()
    {
        IsAllChecked = !IsAllChecked;
    }

    public async Task InstallSongsCommandAsync()
    {
        var selectedInstallItems = IngestionQueue
            .Where(item => item.Checked && item.CanInstall && _serverQueueJobIds.ContainsKey(item.IngestionId))
            .ToList();

        if (selectedInstallItems.Count == 0)
        {
            return;
        }

        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            Logger.LogWarning("Install", "Install request ignored because ChartHub.Server connection is not configured.");
            return;
        }

        foreach (IngestionQueueItem item in selectedInstallItems)
        {
            if (!_serverQueueJobIds.TryGetValue(item.IngestionId, out Guid jobId))
            {
                continue;
            }

            try
            {
                await _serverApiClient.RequestInstallDownloadJobAsync(baseUrl, bearerToken, jobId);
                item.Checked = false;
            }
            catch (Exception ex)
            {
                Logger.LogError("Install", "Failed to submit install request", ex, new Dictionary<string, object?>
                {
                    ["jobId"] = jobId,
                    ["displayName"] = item.DisplayName,
                });
            }
        }

        if (_cloneHeroViewModel is not null)
        {
            await _cloneHeroViewModel.RefreshAsync();
        }
    }

    private async Task DeleteJobAsync(IngestionQueueItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (!_serverQueueJobIds.TryGetValue(item.IngestionId, out Guid jobId))
        {
            Logger.LogWarning("Download", "Cannot delete queue item: no server job ID found.", new Dictionary<string, object?>
            {
                ["ingestionId"] = item.IngestionId,
            });
            return;
        }

        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            return;
        }

        try
        {
            await _serverApiClient.DeleteDownloadJobAsync(baseUrl, bearerToken, jobId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError("Download", "Failed to delete server download job", ex, new Dictionary<string, object?>
            {
                ["jobId"] = jobId,
            });
            return;
        }

        await _uiInvoke(() =>
        {
            _serverQueueJobIds.Remove(item.IngestionId);
            IngestionQueue.Remove(item);
        }).ConfigureAwait(false);
    }

    private async Task ToggleJobLogAsync(IngestionQueueItem? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsJobLogExpanded = !item.IsJobLogExpanded;

        if (!item.IsJobLogExpanded)
        {
            return;
        }

        if (item.JobLogs.Count > 0)
        {
            return;
        }

        if (!_serverQueueJobIds.TryGetValue(item.IngestionId, out Guid jobId))
        {
            return;
        }

        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            return;
        }

        item.IsLoadingLogs = true;
        try
        {
            IReadOnlyList<ChartHubServerJobLogEntry> logs = await _serverApiClient
                .GetJobLogsAsync(baseUrl, bearerToken, jobId)
                .ConfigureAwait(false);

            await _uiInvoke(() =>
            {
                item.JobLogs.Clear();
                foreach (ChartHubServerJobLogEntry entry in logs)
                {
                    item.JobLogs.Add(entry);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError("Download", "Failed to fetch job logs", ex, new Dictionary<string, object?>
            {
                ["jobId"] = jobId,
            });
        }
        finally
        {
            item.IsLoadingLogs = false;
        }
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public async ValueTask DisposeAsync()
    {
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
            if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
            {
                _serverQueueJobIds.Clear();
                await _uiInvoke(() => IngestionQueue.Clear()).ConfigureAwait(false);
                return;
            }

            try
            {
                IReadOnlyList<ChartHubServerDownloadJobResponse> jobs = await _serverApiClient
                    .ListDownloadJobsAsync(baseUrl, bearerToken, cancellationToken)
                    .ConfigureAwait(false);

                await MergeServerJobsAsync(jobs, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsUnauthorizedServerError(ex))
            {
                HandleUnauthorizedServerError("Download", "ChartHub.Server token rejected during queue refresh; clearing local token.", baseUrl);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task MergeServerJobsAsync(IReadOnlyList<ChartHubServerDownloadJobResponse> allJobs, CancellationToken cancellationToken)
    {
        IEnumerable<ChartHubServerDownloadJobResponse> filtered = allJobs.Where(job =>
            !string.Equals(job.Stage, "Installed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(job.Stage, "Completed", StringComparison.OrdinalIgnoreCase));

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

        var sortedJobs = filtered.ToList();

        var existingByIngestionId = new Dictionary<long, IngestionQueueItem>();
        foreach (IngestionQueueItem item in IngestionQueue)
        {
            existingByIngestionId[item.IngestionId] = item;
        }

        var newQueue = new List<IngestionQueueItem>(sortedJobs.Count);
        var newServerQueueJobIds = new Dictionary<long, Guid>(sortedJobs.Count);

        foreach (ChartHubServerDownloadJobResponse job in sortedJobs)
        {
            long ingestionId = BitConverter.ToInt64(job.JobId.ToByteArray(), 0);
            newServerQueueJobIds[ingestionId] = job.JobId;

            if (existingByIngestionId.TryGetValue(ingestionId, out IngestionQueueItem? existing))
            {
                UpdateItemFromServerJob(existing, job);
                newQueue.Add(existing);
            }
            else
            {
                var item = new IngestionQueueItem
                {
                    IngestionId = ingestionId,
                    Source = job.Source,
                    SourceId = job.SourceId,
                    SourceLink = job.SourceUrl,
                    DisplayName = job.DisplayName,
                    LibrarySource = job.Source,
                };
                UpdateItemFromServerJob(item, job);
                newQueue.Add(item);
            }
        }

        await _uiInvoke(() =>
        {
            _serverQueueJobIds.Clear();
            foreach (KeyValuePair<long, Guid> kv in newServerQueueJobIds)
            {
                _serverQueueJobIds[kv.Key] = kv.Value;
            }

            ApplySharedDownloadJobs(allJobs);

            var newIds = newQueue.Select(x => x.IngestionId).ToHashSet();
            for (int i = IngestionQueue.Count - 1; i >= 0; i--)
            {
                if (!newIds.Contains(IngestionQueue[i].IngestionId))
                {
                    IngestionQueue.RemoveAt(i);
                }
            }

            for (int i = 0; i < newQueue.Count; i++)
            {
                if (i < IngestionQueue.Count)
                {
                    if (IngestionQueue[i].IngestionId != newQueue[i].IngestionId)
                    {
                        IngestionQueue.RemoveAt(i);
                        IngestionQueue.Insert(i, newQueue[i]);
                    }
                }
                else
                {
                    IngestionQueue.Add(newQueue[i]);
                }
            }

            IsAnyChecked = AnyItemChecked();
        }).ConfigureAwait(false);
    }

    private static void UpdateItemFromServerJob(IngestionQueueItem item, ChartHubServerDownloadJobResponse job)
    {
        item.CurrentState = MapServerStage(job.Stage);
        item.ProgressPercent = Math.Clamp(job.ProgressPercent, 0, 100);
        item.DownloadedLocation = job.DownloadedPath;
        item.InstalledLocation = job.InstalledPath;
        item.DesktopState = ResolveDesktopState(job);
        item.UpdatedAtUtc = job.UpdatedAtUtc;
        item.Charter = job.Charter;
        item.Artist = job.Artist;
        item.Title = job.Title;
        item.FileType = job.FileType;
        item.ConversionWarningMessage = GetPrimaryConversionWarning(job);
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
            card.WarningMessage = GetPrimaryConversionWarning(job);

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

    private void HandleUnauthorizedServerError(string logCategory, string message, string? baseUrl = null)
    {
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

    private static string? GetPrimaryConversionWarning(ChartHubServerDownloadJobResponse job)
    {
        string? message = job.ConversionStatuses?
            .Select(status => status.Message?.Trim())
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

        return string.IsNullOrWhiteSpace(message)
            ? null
            : $"Warning: {message}";
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

                _statusBarService.Clear();

                await foreach (IReadOnlyList<ChartHubServerDownloadJobResponse> jobs in _serverApiClient
                                   .StreamDownloadJobsAsync(baseUrl, bearerToken, cancellationToken)
                                   .WithCancellation(cancellationToken)
                                   .ConfigureAwait(false))
                {
                    await MergeServerJobsAsync(jobs, cancellationToken).ConfigureAwait(false);
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
                _statusBarService.Post(UiLocalization.Get("StatusBar.ServerUnavailable"));
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }
}
