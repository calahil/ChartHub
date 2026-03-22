using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Services.Transfers;
using ChartHub.Strings;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

namespace ChartHub.ViewModels;

public sealed class EncoreViewModel : INotifyPropertyChanged
{
    public bool IsCompanionMode => OperatingSystem.IsAndroid();
    public bool IsDesktopMode => !OperatingSystem.IsAndroid();
    private readonly EncoreApiService _apiService;
    private readonly ITransferOrchestrator _transferOrchestrator;
    private readonly ISettingsOrchestrator _settingsOrchestrator;
    private readonly Dictionary<DownloadFile, CancellationTokenSource> _downloadTokens = [];
    private readonly object _stateSaveSync = new();

    private string _searchText = string.Empty;
    private bool _isLoading;
    private bool _noResults;
    private bool _isPlaceholder = true;
    private bool _isAdvancedVisible;
    private string? _selectedInstrument;
    private string? _selectedDifficulty;
    private string? _selectedDrumType;
    private bool _drumsReviewed = true;
    private string _selectedSort = "name";
    private string _selectedSortDirection = "asc";
    private EncoreSong? _selectedSong;
    private string _advancedName = string.Empty;
    private string _advancedArtist = string.Empty;
    private string _advancedAlbum = string.Empty;
    private string _advancedGenre = string.Empty;
    private string _advancedYear = string.Empty;
    private string _advancedCharter = string.Empty;
    private string _minYear = string.Empty;
    private string _maxYear = string.Empty;
    private string _minLength = string.Empty;
    private string _maxLength = string.Empty;
    private bool? _hasVideoBackground;
    private bool? _hasLyrics;
    private bool? _hasVocals;
    private bool? _has2xKick;
    private bool? _hasIssues;
    private bool? _modchart;
    private bool _isRestoringState;
    private CancellationTokenSource? _stateSaveDebounceCts;

    public EncorePageStrings PageStrings { get; } = new();

    public ObservableCollection<EncoreSong> DataItems { get; } = [];
    public ObservableCollection<DownloadFile> Downloads { get; private set; } = [];

    public List<string?> Instruments { get; } = [null, "guitar", "bass", "drums", "vocals", "keys"];
    public List<string?> Difficulties { get; } = [null, "0", "1", "2", "3", "4", "5", "6"];
    public List<string?> DrumTypes { get; } = [null, "pro-drums", "five-lane"];

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public bool NoResults
    {
        get => _noResults;
        set
        {
            _noResults = value;
            OnPropertyChanged();
        }
    }

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

    public bool HasResults => !IsPlaceholder && DataItems.Count > 0;

    public bool IsAdvancedVisible
    {
        get => _isAdvancedVisible;
        set
        {
            _isAdvancedVisible = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string? SelectedInstrument
    {
        get => _selectedInstrument;
        set
        {
            _selectedInstrument = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string? SelectedDifficulty
    {
        get => _selectedDifficulty;
        set
        {
            _selectedDifficulty = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string? SelectedDrumType
    {
        get => _selectedDrumType;
        set
        {
            _selectedDrumType = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public bool DrumsReviewed
    {
        get => _drumsReviewed;
        set
        {
            _drumsReviewed = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            _selectedSort = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string SelectedSortDirection
    {
        get => _selectedSortDirection;
        set
        {
            _selectedSortDirection = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public EncoreSong? SelectedSong
    {
        get => _selectedSong;
        set
        {
            _selectedSong = value;
            OnPropertyChanged();
        }
    }

    public string AdvancedName
    {
        get => _advancedName;
        set
        {
            _advancedName = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string AdvancedArtist
    {
        get => _advancedArtist;
        set
        {
            _advancedArtist = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string AdvancedCharter
    {
        get => _advancedCharter;
        set
        {
            _advancedCharter = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string AdvancedAlbum
    {
        get => _advancedAlbum;
        set
        {
            _advancedAlbum = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string AdvancedGenre
    {
        get => _advancedGenre;
        set
        {
            _advancedGenre = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string AdvancedYear
    {
        get => _advancedYear;
        set
        {
            _advancedYear = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string MinYear
    {
        get => _minYear;
        set
        {
            _minYear = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string MaxYear
    {
        get => _maxYear;
        set
        {
            _maxYear = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string MinLength
    {
        get => _minLength;
        set
        {
            _minLength = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public string MaxLength
    {
        get => _maxLength;
        set
        {
            _maxLength = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public bool? HasVideoBackground
    {
        get => _hasVideoBackground;
        set
        {
            _hasVideoBackground = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public bool? HasLyrics
    {
        get => _hasLyrics;
        set
        {
            _hasLyrics = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public bool? HasVocals
    {
        get => _hasVocals;
        set
        {
            _hasVocals = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public bool? Has2xKick
    {
        get => _has2xKick;
        set
        {
            _has2xKick = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public bool? HasIssues
    {
        get => _hasIssues;
        set
        {
            _hasIssues = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public bool? Modchart
    {
        get => _modchart;
        set
        {
            _modchart = value;
            OnPropertyChanged();
            ScheduleStateSave();
        }
    }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand LoadMoreCommand { get; }
    public IAsyncRelayCommand<EncoreSong?> DownloadSongCommand { get; }
    public IRelayCommand ToggleAdvancedCommand { get; }
    public IRelayCommand<DownloadFile?> CancelDownloadCommand { get; }

    public EncoreViewModel(
        EncoreApiService apiService,
        ITransferOrchestrator transferOrchestrator,
        ISettingsOrchestrator settingsOrchestrator,
        SharedDownloadQueue sharedDownloadQueue)
    {
        _apiService = apiService;
        _transferOrchestrator = transferOrchestrator;
        _settingsOrchestrator = settingsOrchestrator;

        Downloads = sharedDownloadQueue.Downloads;

        RestoreState(_settingsOrchestrator.Current.EncoreUi);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync);
        DownloadSongCommand = new AsyncRelayCommand<EncoreSong?>(DownloadSongAsync);
        ToggleAdvancedCommand = new RelayCommand(() => IsAdvancedVisible = !IsAdvancedVisible);
        CancelDownloadCommand = new RelayCommand<DownloadFile?>(CancelDownload);

        ObserveBackgroundTask(RefreshAsync(), "Encore initial load");
    }

    private void ScheduleStateSave()
    {
        if (_isRestoringState)
        {
            return;
        }

        CancellationTokenSource cts;
        lock (_stateSaveSync)
        {
            _stateSaveDebounceCts?.Cancel();
            _stateSaveDebounceCts?.Dispose();
            _stateSaveDebounceCts = new CancellationTokenSource();
            cts = _stateSaveDebounceCts;
        }

        ObserveBackgroundTask(PersistStateAfterDelayAsync(cts.Token), "Persist Encore UI state");
    }

    private async Task PersistStateAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken);

            EncoreUiStateConfig snapshot = CaptureState();
            ConfigValidationResult result = await _settingsOrchestrator.UpdateAsync(config =>
            {
                config.EncoreUi = snapshot;
            }, cancellationToken);

            if (!result.IsValid)
            {
                Logger.LogWarning("Config", "Failed to persist Encore UI state", new Dictionary<string, object?>
                {
                    ["failureCount"] = result.Failures.Count,
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Newer state update superseded this save.
        }
    }

    private void RestoreState(EncoreUiStateConfig? state)
    {
        if (state is null)
        {
            return;
        }

        _isRestoringState = true;
        try
        {
            SearchText = state.SearchText ?? string.Empty;
            IsAdvancedVisible = state.IsAdvancedVisible;
            SelectedInstrument = state.SelectedInstrument;
            SelectedDifficulty = state.SelectedDifficulty;
            SelectedDrumType = state.SelectedDrumType;
            DrumsReviewed = state.DrumsReviewed;
            SelectedSort = string.IsNullOrWhiteSpace(state.SelectedSort) ? "name" : state.SelectedSort;
            SelectedSortDirection = string.IsNullOrWhiteSpace(state.SelectedSortDirection) ? "asc" : state.SelectedSortDirection;
            AdvancedName = state.AdvancedName ?? string.Empty;
            AdvancedArtist = state.AdvancedArtist ?? string.Empty;
            AdvancedAlbum = state.AdvancedAlbum ?? string.Empty;
            AdvancedGenre = state.AdvancedGenre ?? string.Empty;
            AdvancedYear = state.AdvancedYear ?? string.Empty;
            AdvancedCharter = state.AdvancedCharter ?? string.Empty;
            MinYear = state.MinYear ?? string.Empty;
            MaxYear = state.MaxYear ?? string.Empty;
            MinLength = state.MinLength ?? string.Empty;
            MaxLength = state.MaxLength ?? string.Empty;
            HasVideoBackground = state.HasVideoBackground;
            HasLyrics = state.HasLyrics;
            HasVocals = state.HasVocals;
            Has2xKick = state.Has2xKick;
            HasIssues = state.HasIssues;
            Modchart = state.Modchart;
        }
        finally
        {
            _isRestoringState = false;
        }
    }

    private EncoreUiStateConfig CaptureState()
    {
        return new EncoreUiStateConfig
        {
            SearchText = SearchText,
            IsAdvancedVisible = IsAdvancedVisible,
            SelectedInstrument = SelectedInstrument,
            SelectedDifficulty = SelectedDifficulty,
            SelectedDrumType = SelectedDrumType,
            DrumsReviewed = DrumsReviewed,
            SelectedSort = SelectedSort,
            SelectedSortDirection = SelectedSortDirection,
            AdvancedName = AdvancedName,
            AdvancedArtist = AdvancedArtist,
            AdvancedAlbum = AdvancedAlbum,
            AdvancedGenre = AdvancedGenre,
            AdvancedYear = AdvancedYear,
            AdvancedCharter = AdvancedCharter,
            MinYear = MinYear,
            MaxYear = MaxYear,
            MinLength = MinLength,
            MaxLength = MaxLength,
            HasVideoBackground = HasVideoBackground,
            HasLyrics = HasLyrics,
            HasVocals = HasVocals,
            Has2xKick = Has2xKick,
            HasIssues = HasIssues,
            Modchart = Modchart,
        };
    }

    public async Task RefreshAsync()
    {
        if (IsLoading)
        {
            return;
        }

        DataItems.Clear();
        NoResults = false;
        IsPlaceholder = true;
        await ExecuteSearchAsync(reset: true);
    }

    public async Task LoadMoreAsync()
    {
        if (IsLoading || !_apiService.HasMoreRecords)
        {
            return;
        }

        _apiService.CurrentPage += 1;
        await ExecuteSearchAsync(reset: false);
    }

    public async Task DownloadSongAsync(EncoreSong? song)
    {
        EncoreSong? selected = song ?? SelectedSong;
        if (selected is null)
        {
            return;
        }

        string fileName = BuildEncoreFileName(selected);
        var proxySong = selected.ToViewSong(fileName);

        var downloadItem = new DownloadFile(
            fileName,
            Path.GetTempPath(),
            selected.DownloadUrl,
            null);
        downloadItem.SourceName = "Encore";
        downloadItem.CancelAction = () => CancelDownload(downloadItem);

        var cts = new CancellationTokenSource();
        _downloadTokens[downloadItem] = cts;

        TransferResult result = await _transferOrchestrator.QueueSongDownloadAsync(proxySong, downloadItem, Downloads, cts.Token);
        if (result.Success)
        {
            selected.IsInLibrary = false;
        }
        else if (!string.IsNullOrWhiteSpace(result.Error))
        {
            Logger.LogWarning("Encore", "Encore transfer reported failure", new Dictionary<string, object?>
            {
                ["displayName"] = downloadItem.DisplayName,
                ["error"] = result.Error,
                ["status"] = downloadItem.Status,
            });
        }

        _downloadTokens.Remove(downloadItem);
        cts.Dispose();
    }

    private void CancelDownload(DownloadFile? downloadItem)
    {
        if (downloadItem is null)
        {
            return;
        }

        if (_downloadTokens.TryGetValue(downloadItem, out CancellationTokenSource? cts))
        {
            downloadItem.Status = TransferStage.Cancelling.ToString();
            downloadItem.ErrorMessage = null;
            cts.Cancel();
        }
    }

    private async Task ExecuteSearchAsync(bool reset)
    {
        IsLoading = true;
        try
        {
            ObservableCollection<EncoreSong> results;
            if (HasAdvancedFilters())
            {
                results = await _apiService.AdvancedSearchAsync(reset, BuildAdvancedRequest());
            }
            else
            {
                results = await _apiService.SearchAsync(reset, BuildGeneralRequest());
            }

            if (reset)
            {
                DataItems.Clear();
            }

            foreach (EncoreSong item in results)
            {
                if (!DataItems.Contains(item))
                {
                    DataItems.Add(item);
                }
            }

            NoResults = DataItems.Count == 0;
        }
        finally
        {
            IsLoading = false;
            IsPlaceholder = false;
        }
    }

    private EncoreGeneralSearchRequest BuildGeneralRequest()
    {
        return new EncoreGeneralSearchRequest
        {
            Search = string.IsNullOrWhiteSpace(SearchText) ? "*" : SearchText,
            Instrument = SelectedInstrument,
            Difficulty = SelectedDifficulty,
            DrumType = SelectedDrumType,
            DrumsReviewed = DrumsReviewed,
            Sort = new EncoreSortOption
            {
                Type = SelectedSort,
                Direction = SelectedSortDirection,
            },
        };
    }

    private EncoreAdvancedSearchRequest BuildAdvancedRequest()
    {
        return new EncoreAdvancedSearchRequest
        {
            Search = string.IsNullOrWhiteSpace(SearchText) ? "*" : SearchText,
            Instrument = SelectedInstrument,
            Difficulty = SelectedDifficulty,
            DrumType = SelectedDrumType,
            DrumsReviewed = DrumsReviewed,
            Sort = new EncoreSortOption
            {
                Type = SelectedSort,
                Direction = SelectedSortDirection,
            },
            Name = BuildTextFilter(AdvancedName),
            Artist = BuildTextFilter(AdvancedArtist),
            Album = BuildTextFilter(AdvancedAlbum),
            Genre = BuildTextFilter(AdvancedGenre),
            Year = BuildTextFilter(AdvancedYear),
            Charter = BuildTextFilter(AdvancedCharter),
            MinYear = ParseNullableInt(MinYear),
            MaxYear = ParseNullableInt(MaxYear),
            MinLength = ParseNullableInt(MinLength),
            MaxLength = ParseNullableInt(MaxLength),
            HasVideoBackground = HasVideoBackground,
            HasLyrics = HasLyrics,
            HasVocals = HasVocals,
            Has2xKick = Has2xKick,
            HasIssues = HasIssues,
            Modchart = Modchart,
        };
    }

    private static EncoreTextFilter? BuildTextFilter(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : new EncoreTextFilter { Value = value.Trim(), Exact = false, Exclude = false };
    }

    private bool HasAdvancedFilters()
    {
        return !string.IsNullOrWhiteSpace(AdvancedName)
            || !string.IsNullOrWhiteSpace(AdvancedArtist)
            || !string.IsNullOrWhiteSpace(AdvancedAlbum)
            || !string.IsNullOrWhiteSpace(AdvancedGenre)
            || !string.IsNullOrWhiteSpace(AdvancedYear)
            || !string.IsNullOrWhiteSpace(AdvancedCharter)
            || !string.IsNullOrWhiteSpace(MinYear)
            || !string.IsNullOrWhiteSpace(MaxYear)
            || !string.IsNullOrWhiteSpace(MinLength)
            || !string.IsNullOrWhiteSpace(MaxLength)
            || HasVideoBackground.HasValue
            || HasLyrics.HasValue
            || HasVocals.HasValue
            || Has2xKick.HasValue
            || HasIssues.HasValue
            || Modchart.HasValue;
    }

    private static int? ParseNullableInt(string value)
    {
        if (int.TryParse(value, out int parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string BuildEncoreFileName(EncoreSong song)
    {
        string artist = string.IsNullOrWhiteSpace(song.Artist) ? "Unknown Artist" : song.Artist;
        string title = string.IsNullOrWhiteSpace(song.Name) ? "Unknown Song" : song.Name;
        return SafePathHelper.SanitizeFileName($"{artist} - {title}.sng", "encore-chart.sng");
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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}