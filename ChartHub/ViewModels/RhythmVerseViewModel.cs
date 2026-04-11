using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Threading;

using ChartHub.Configuration.Interfaces;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Strings;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Configuration;

namespace ChartHub.ViewModels;

public class InstrumentItem : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private string _value = string.Empty;

    public string DisplayName
    {
        get => _displayName;
        set
        {
            _displayName = value;
            OnPropertyChanged();
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            _value = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RhythmVerseViewModel : INotifyPropertyChanged
{
    public bool IsCompanionMode => OperatingSystem.IsAndroid();
    public bool IsDesktopMode => !OperatingSystem.IsAndroid();

    public enum PaneMode
    {
        None,
        Filters,
        Downloads
    }

    private PaneMode _activePane;
    public PaneMode ActivePane
    {
        get => _activePane;
        set
        {
            _activePane = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(isPaneOpen));
            OnPropertyChanged(nameof(IsFiltersPaneVisible));
            OnPropertyChanged(nameof(IsDownloadsPaneVisible));
        }
    }
    public bool IsFiltersPaneVisible => ActivePane == PaneMode.Filters;
    public bool IsDownloadsPaneVisible => ActivePane == PaneMode.Downloads;
    public bool isPaneOpen => ActivePane != PaneMode.None;
    public ICommand ShowFilterPaneCommand { get; }
    public ICommand ShowDownloadsPaneCommand { get; }

    private readonly IChartHubServerApiClient _serverApiClient;
    private readonly ISettingsOrchestrator _settingsOrchestrator;
    private readonly LibraryCatalogService _libraryCatalog;
    private readonly SemaphoreSlim _loadDataGate = new(1, 1);
    private int _loadMoreGate;
    private readonly Func<Action, Task> _uiInvoke;

    private ApiClientService _apiClient;
    public ApiClientService ApiClient
    {
        get => _apiClient;
        set
        {
            _apiClient = value;
            OnPropertyChanged();
        }
    }

    public long? RecordsPerPage
    {
        get { return ApiClient.RecordsPerPage; }
    }

    public bool HasMoreRecords
    {
        get { return ApiClient.HasMoreRecords; }
    }

    public long? TotalPages
    {
        get { return ApiClient.TotalPages; }
    }

    public long? TotalResults
    {
        get { return ApiClient.TotalResults; }
    }

    public long? CurrentPage
    {
        get { return ApiClient.CurrentPage; }
        set
        {
            if (value != null && value > 0 && value <= TotalPages)
            {
                ApiClient.CurrentPage = value;
                ApiClient.HasMoreRecords = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentPage));
                if (DataItems != null)
                {
                    DataItems.Clear();
                }
                else
                {
                    DataItems = [];
                }
                IsLoading = false;
                IsPlaceholder = true;
                NoResults = false;
                ObserveBackgroundTask(LoadDataAsync(false), "RhythmVerse page load");
            }
        }
    }

    public long? StartRecord
    {
        get { return ApiClient.StartRecord; }
    }

    public long? EndRecord
    {
        get { return ApiClient.EndRecord; }
    }

    private bool _noResults;
    public bool NoResults
    {
        get => _noResults;
        set
        {
            _noResults = value;
            OnPropertyChanged();
        }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    private bool _isAuthorFiltered;
    public bool IsAuthorFiltered
    {
        get => _isAuthorFiltered;
        set
        {
            _isAuthorFiltered = value;
            OnPropertyChanged();
        }
    }



    private bool _isPlaceholder;
    public bool IsPlaceholder
    {
        get => _isPlaceholder;
        set
        {
            _isPlaceholder = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasResults));
        }
    }

    public bool HasResults => !IsPlaceholder;

    private string _selectedFilter;
    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (_selectedFilter != value)
            {
                _selectedFilter = value;
                OnPropertyChanged();
            }
        }
    }

    private string _selectedOrder;
    public string SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (_selectedOrder != value)
            {
                _selectedOrder = value;
                OnPropertyChanged();
            }
        }
    }

    private string _searchAuthorText;
    public string SearchAuthorText
    {
        get => _searchAuthorText;
        set
        {
            _searchAuthorText = value;
            OnPropertyChanged();
        }
    }

    private string _searchText;
    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
        }
    }

    private ObservableCollection<ViewSong>? _dataItems;
    public ObservableCollection<ViewSong>? DataItems
    {
        get => _dataItems;
        set
        {
            _dataItems = value;
            OnPropertyChanged();
        }
    }

    private ObservableCollection<InstrumentItem> _selectedInstruments;
    public ObservableCollection<InstrumentItem> SelectedInstruments
    {
        get => _selectedInstruments;
        set
        {
            _selectedInstruments = value;
            OnPropertyChanged();
        }
    }

    private ObservableCollection<DownloadFile> _downloads;
    public ObservableCollection<DownloadFile> Downloads
    {
        get => _downloads;
        set
        {
            _downloads = value;
            OnPropertyChanged();
        }
    }

    public bool HasActiveDownloads => Downloads.Count > 0;
    public bool NoActiveDownloads => Downloads.Count == 0;

    private ViewSong? _selectedFile;
    public ViewSong? SelectedFile
    {
        get => _selectedFile;
        set
        {
            _selectedFile = value;
            OnPropertyChanged();
        }
    }

    public List<string> Filters { get; set; }
    public List<string> Orders { get; set; }

    public ObservableCollection<InstrumentItem> Instruments { get; set; }


    public IAsyncRelayCommand RefreshButtonCommand { get; }
    public IAsyncRelayCommand<ViewSong?> DownloadFileCommand { get; }
    public IAsyncRelayCommand LoadMoreCommand { get; }
    public IAsyncRelayCommand<ViewSong?> ViewCreatorCommand { get; }
    public IRelayCommand<DownloadFile?> CancelDownloadCommand { get; }
    public IRelayCommand<DownloadFile?> ClearDownloadCommand { get; }

    public RhythmVersePageStrings PageStrings { get; }

    public RhythmVerseViewModel(
        IConfiguration configuration,
        LibraryCatalogService libraryCatalog,
        SharedDownloadQueue sharedDownloadQueue,
        ISettingsOrchestrator settingsOrchestrator,
        IChartHubServerApiClient serverApiClient)
        : this(
            new ApiClientService(configuration),
            libraryCatalog,
            sharedDownloadQueue,
            settingsOrchestrator,
            serverApiClient,
            loadInitialData: true,
            uiInvoke: async action => await Dispatcher.UIThread.InvokeAsync(action))
    {
    }

    internal RhythmVerseViewModel(
        ApiClientService apiClient,
        LibraryCatalogService libraryCatalog,
        SharedDownloadQueue sharedDownloadQueue,
        ISettingsOrchestrator settingsOrchestrator,
        IChartHubServerApiClient serverApiClient,
        bool loadInitialData,
        Func<Action, Task> uiInvoke)
    {
        _uiInvoke = uiInvoke;
        _libraryCatalog = libraryCatalog;
        _settingsOrchestrator = settingsOrchestrator;
        _serverApiClient = serverApiClient;
        PageStrings = new RhythmVersePageStrings();
        _apiClient = apiClient;
        _dataItems = [];
        _downloads = sharedDownloadQueue.Downloads;
        Filters = PageStrings.Filters;
        Orders = PageStrings.Orders;
        _selectedFilter = Filters[0];
        _selectedOrder = Orders[0];
        _searchAuthorText = string.Empty;
        _searchText = string.Empty;
        _isAuthorFiltered = false;
        _isLoading = false;
        IsPlaceholder = true;
        NoResults = false;
        RefreshButtonCommand = new AsyncRelayCommand(RefreshButton);
        DownloadFileCommand = new AsyncRelayCommand<ViewSong?>(
            DownloadFile,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync);
        ViewCreatorCommand = new AsyncRelayCommand<ViewSong?>(ViewCreator);
        CancelDownloadCommand = new RelayCommand<DownloadFile?>(CancelDownload);
        ClearDownloadCommand = new RelayCommand<DownloadFile?>(ClearDownload);
        _activePane = PaneMode.None;
        ShowFilterPaneCommand = new RelayCommand(() =>
        {
            ActivePane = ActivePane == PaneMode.Filters ? PaneMode.None : PaneMode.Filters;
        });

        ShowDownloadsPaneCommand = new RelayCommand(() =>
        {
            ActivePane = ActivePane == PaneMode.Downloads ? PaneMode.None : PaneMode.Downloads;
        });
        Instruments =
        [
            new InstrumentItem { DisplayName = "None", Value = string.Empty },
            new InstrumentItem { DisplayName = "Bass", Value = "bass" },
            new InstrumentItem { DisplayName = "Bass (GHL 6 Fret)", Value = "bassghl" },
            new InstrumentItem { DisplayName = "Drums", Value = "drums" },
            new InstrumentItem { DisplayName = "Guitar", Value = "guitar" },
            new InstrumentItem { DisplayName = "Guitar (GHL 6 Fret)", Value = "guitarghl" },
            new InstrumentItem { DisplayName = "Keys", Value = "keys" },
            new InstrumentItem { DisplayName = "Pro Keys", Value = "prokeys" },
            new InstrumentItem { DisplayName = "Vocals", Value = "vocals" },
            new InstrumentItem { DisplayName = "Guitar Co-Op", Value = "guitar_coop" },
            new InstrumentItem { DisplayName = "Co-op (Unspecified)", Value = "guitarcoop" },
            new InstrumentItem { DisplayName = "Pro Bass", Value = "probass" },
            new InstrumentItem { DisplayName = "Real Drums", Value = "prodrums" },
            new InstrumentItem { DisplayName = "Pro Guitar", Value = "proguitar" },
            new InstrumentItem { DisplayName = "Rhythm Guitar", Value = "rhythm" },
        ];
        _selectedInstruments = [Instruments[0]];

        _apiClient.PropertyChanged += ApiClient_PropertyChanged;
        _downloads.CollectionChanged += Downloads_CollectionChanged;
        foreach (DownloadFile item in _downloads)
        {
            item.PropertyChanged += DownloadItem_PropertyChanged;
        }
        if (loadInitialData)
        {
            ObserveBackgroundTask(LoadDataAsync(true), "RhythmVerse initial load");
        }
    }

    private void Downloads_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (DownloadFile item in e.NewItems)
            {
                item.PropertyChanged += DownloadItem_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (DownloadFile item in e.OldItems)
            {
                item.PropertyChanged -= DownloadItem_PropertyChanged;
            }
        }

        OnPropertyChanged(nameof(HasActiveDownloads));
        OnPropertyChanged(nameof(NoActiveDownloads));
    }

    private void DownloadItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChartHub.Services.DownloadFile.Status)
            || e.PropertyName == nameof(ChartHub.Services.DownloadFile.DownloadProgress)
            || e.PropertyName == nameof(ChartHub.Services.DownloadFile.ErrorMessage))
        {
            OnPropertyChanged(nameof(HasActiveDownloads));
            OnPropertyChanged(nameof(NoActiveDownloads));
        }
    }

    private static void ObserveBackgroundTask(Task task, string context)
    {
        _ = task.ContinueWith(t =>
        {
            Exception? ex = t.Exception?.GetBaseException();
            if (ex is not null)
            {
                Logger.LogError("App", $"{context} failed", ex);
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void ApiClient_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Raise the PropertyChanged event for the corresponding property in the ViewModel
        OnPropertyChanged(e.PropertyName);
    }

    public async Task RefreshButton()
    {
        if (DataItems != null)
        {
            DataItems.Clear();
        }
        else
        {
            DataItems = [];
        }
        ApiClient.CurrentPage = 1;
        ApiClient.HasMoreRecords = true;
        IsLoading = false;
        IsPlaceholder = true;
        NoResults = false;
        await LoadDataAsync(true);
    }

    public async Task DownloadFile(ViewSong? song)
    {
        if (song is null)
        {
            Logger.LogWarning("ServerDownload", "Cannot queue RhythmVerse download because the command parameter was null.");
            return;
        }

        string sourceId = song.SourceId?.Trim() ?? string.Empty;
        string sourceUrl = song.DownloadLink?.Trim() ?? string.Empty;
        string displayName = song.Title ?? song.FileName ?? "Unknown";
        long? fileSize = song.FileSize;

        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            Logger.LogWarning("ServerDownload", "Cannot queue RhythmVerse download because sync API endpoint/token is not configured.");
            return;
        }

        if (string.IsNullOrWhiteSpace(sourceUrl) || string.IsNullOrWhiteSpace(sourceId))
        {
            Logger.LogWarning("ServerDownload", "Cannot queue RhythmVerse download due to missing source metadata.", new Dictionary<string, object?>
            {
                ["sourceId"] = sourceId,
                ["downloadLinkPresent"] = !string.IsNullOrWhiteSpace(sourceUrl),
            });
            return;
        }

        var job = new PendingRhythmVerseJob(
            SourceId: sourceId,
            DisplayName: displayName,
            SourceUrl: sourceUrl,
            FileSize: fileSize);
        await QueueServerDownloadJobAsync(song, job, baseUrl, bearerToken).ConfigureAwait(false);
    }

    private async Task QueueServerDownloadJobAsync(ViewSong song, PendingRhythmVerseJob pendingJob, string baseUrl, string bearerToken)
    {
        var request = new ChartHubServerCreateDownloadJobRequest(
            Source: "RhythmVerse",
            SourceId: pendingJob.SourceId,
            DisplayName: pendingJob.DisplayName,
            SourceUrl: pendingJob.SourceUrl);

        try
        {
            ChartHubServerDownloadJobResponse jobResponse = await _serverApiClient
                .CreateDownloadJobAsync(baseUrl, bearerToken, request)
                .ConfigureAwait(false);

            await _uiInvoke(() =>
            {
                DownloadFile? existing = Downloads.FirstOrDefault(item =>
                    string.Equals(item.Url, jobResponse.JobId.ToString("D"), StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    var queuedItem = new DownloadFile(
                        jobResponse.DisplayName,
                        jobResponse.DownloadedPath ?? Path.GetTempPath(),
                        jobResponse.JobId.ToString("D"),
                        pendingJob.FileSize)
                    {
                        SourceName = jobResponse.Source,
                        Status = jobResponse.Stage,
                        DownloadProgress = Math.Clamp(jobResponse.ProgressPercent, 0, 100),
                        Finished = IsTerminalServerStage(jobResponse.Stage),
                        ErrorMessage = jobResponse.Error,
                    };
                    Downloads.Insert(0, queuedItem);
                }
            });

            Logger.LogInfo("ServerDownload", "Queued RhythmVerse download job", new Dictionary<string, object?>
            {
                ["jobId"] = jobResponse.JobId,
                ["source"] = jobResponse.Source,
                ["sourceId"] = jobResponse.SourceId,
                ["displayName"] = jobResponse.DisplayName,
                ["stage"] = jobResponse.Stage,
                ["baseUrl"] = baseUrl,
            });

            await _uiInvoke(() =>
            {
                song.IsInLibrary = false;
            });
        }
        catch (Exception ex)
        {
            Logger.LogError("ServerDownload", "Failed to submit download job to server", ex, new Dictionary<string, object?>
            {
                ["source"] = "RhythmVerse",
                ["sourceId"] = pendingJob.SourceId,
                ["displayName"] = pendingJob.DisplayName,
            });
        }
    }

    private sealed record PendingRhythmVerseJob(
        string SourceId,
        string DisplayName,
        string SourceUrl,
        long? FileSize);

    private bool TryGetServerConnection(out string baseUrl, out string bearerToken)
    {
        string? configuredBaseUrl = _settingsOrchestrator.Current.Runtime.ServerApiBaseUrl;
        baseUrl = NormalizeApiBaseUrl(configuredBaseUrl);
        bearerToken = _settingsOrchestrator.Current.Runtime.ServerApiAuthToken?.Trim() ?? string.Empty;
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

    private static bool IsTerminalServerStage(string stage)
    {
        return string.Equals(stage, "Installed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stage, "Downloaded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stage, "Completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stage, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stage, "Cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private void CancelDownload(DownloadFile? downloadItem)
    {
        if (downloadItem is null)
        {
            return;
        }

        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            return;
        }

        if (!Guid.TryParse(downloadItem.Url, out Guid jobId))
        {
            return;
        }

        downloadItem.Status = "Cancelling";
        downloadItem.ErrorMessage = null;
        Logger.LogInfo("ServerDownload", "Requesting cancel for RhythmVerse download job", new Dictionary<string, object?>
        {
            ["jobId"] = jobId,
            ["displayName"] = downloadItem.DisplayName,
        });
        ObserveBackgroundTask(_serverApiClient.RequestCancelDownloadJobAsync(baseUrl, bearerToken, jobId), "RhythmVerse cancel server download job");
    }

    private void ClearDownload(DownloadFile? downloadItem)
    {
        if (downloadItem is null)
        {
            return;
        }

        Downloads.Remove(downloadItem);
    }
    public async Task ViewCreator(ViewSong? song)
    {
        ViewSong? file = song ?? SelectedFile;
        if (file == null || file.Author == null)
        {
            return;
        }

        SearchAuthorText = file.Author.Shortname;
        await LoadDataAsync(true);
    }

    public async Task LoadMoreAsync()
    {
        if (IsLoading || !ApiClient.HasMoreRecords)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _loadMoreGate, 1, 0) != 0)
        {
            return;
        }

        try
        {
            ApiClient.CurrentPage = (ApiClient.CurrentPage ?? 1) + 1;
            OnPropertyChanged(nameof(CurrentPage));
            await LoadDataAsync(false);
        }
        finally
        {
            Interlocked.Exchange(ref _loadMoreGate, 0);
        }
    }

    public async Task LoadDataAsync(bool search)
    {
        if (!await _loadDataGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await _uiInvoke(() => IsLoading = true);

            if (search)
            {
                // Ensure new searches always execute a payload fetch, even after previous pagination exhaustion.
                ApiClient.CurrentPage = 1;
                ApiClient.HasMoreRecords = true;
            }

            if (!search && !ApiClient.HasMoreRecords)
            {
                NoResults = DataItems == null || DataItems.Count < 1;
                return;
            }

            if (string.IsNullOrEmpty(SelectedFilter) || string.IsNullOrEmpty(SelectedOrder))
            {
                SelectedFilter = "Artist";
                SelectedOrder = "Ascending";
            }
            string filter = Toolbox.ConvertFilter(SelectedFilter);
            string order = Toolbox.GetSortOrder(filter, SelectedOrder);
            var instrument = SelectedInstruments.ToList();
            IReadOnlyList<ViewSong> pageItems = await ApiClient.GetSongFilesAsync(search, SearchText.ToLower(), filter, order, instrument, SearchAuthorText).ConfigureAwait(false);
            await ApplyLibraryMembershipAsync(pageItems).ConfigureAwait(false);

            await _uiInvoke(() =>
            {
                if (search)
                {
                    DataItems = new ObservableCollection<ViewSong>(pageItems);
                }
                else
                {
                    DataItems ??= [];
                    foreach (ViewSong song in pageItems)
                    {
                        if (!DataItems.Contains(song))
                        {
                            DataItems.Add(song);
                        }
                    }
                }

                NoResults = DataItems.Count < 1;
            });

        }
        finally
        {
            await _uiInvoke(() =>
            {
                IsLoading = false;
                IsPlaceholder = false;
            });
            _loadDataGate.Release();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task ApplyLibraryMembershipAsync(IReadOnlyList<ViewSong> songs)
    {
        var keyedSongs = songs
            .Where(song => !string.IsNullOrWhiteSpace(song.SourceName) && !string.IsNullOrWhiteSpace(song.SourceId))
            .Select(song => new
            {
                Song = song,
                SourceKey = LibraryIdentityService.BuildSourceKey(song.SourceName!, song.SourceId),
            })
            .ToArray();

        string[] sourceIds = keyedSongs
            .Select(item => item.SourceKey)
            .ToArray();

        IReadOnlyDictionary<string, bool> membership = await _libraryCatalog.GetMembershipMapAsync(LibrarySourceNames.RhythmVerse, sourceIds).ConfigureAwait(false);
        await _uiInvoke(() =>
        {
            foreach (var keyedSong in keyedSongs)
            {
                keyedSong.Song.SourceId = keyedSong.SourceKey;
            }

            foreach (ViewSong song in songs)
            {
                song.IsInLibrary = !string.IsNullOrWhiteSpace(song.SourceId)
                    && membership.TryGetValue(song.SourceId, out bool isPresent)
                    && isPresent;
            }
        });
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
