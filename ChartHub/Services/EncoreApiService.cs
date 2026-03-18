using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Linq;
using ChartHub.Models;
using ChartHub.Utilities;

namespace ChartHub.Services;

public sealed class EncoreApiService : INotifyPropertyChanged
{
    private const string ApiBaseUrl = "https://api.enchor.us";
    private const string FilesBaseUrl = "https://files.enchor.us";
    private readonly HttpClient _httpClient;
    private readonly LibraryCatalogService _libraryCatalog;

    private ObservableCollection<EncoreSong>? _dataItems;
    private bool _hasMoreRecords = true;
    private int _currentPage = 1;
    private int _totalResults;

    public ObservableCollection<EncoreSong>? DataItems
    {
        get => _dataItems;
        private set
        {
            _dataItems = value;
            OnPropertyChanged();
        }
    }

    public bool HasMoreRecords
    {
        get => _hasMoreRecords;
        private set
        {
            _hasMoreRecords = value;
            OnPropertyChanged();
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            _currentPage = value;
            OnPropertyChanged();
        }
    }

    public int TotalResults
    {
        get => _totalResults;
        private set
        {
            _totalResults = value;
            OnPropertyChanged();
        }
    }

    public int RecordsPerPage { get; } = 25;

    public EncoreApiService(LibraryCatalogService libraryCatalog)
        : this(libraryCatalog, new HttpClient { BaseAddress = new Uri(ApiBaseUrl) })
    {
    }

    internal EncoreApiService(LibraryCatalogService libraryCatalog, HttpClient httpClient)
    {
        _libraryCatalog = libraryCatalog;
        _httpClient = httpClient;
        DataItems = [];
    }

    public async Task<ObservableCollection<EncoreSong>> SearchAsync(
        bool reset,
        EncoreGeneralSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (reset)
        {
            CurrentPage = 1;
            HasMoreRecords = true;
            DataItems ??= [];
            DataItems.Clear();
        }

        request.Page = CurrentPage;
        request.PerPage = RecordsPerPage;

        var response = await _httpClient.PostAsJsonAsync("/search", request, JsonCerealOptions.Instance, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<EncoreSearchResponse>(JsonCerealOptions.Instance, cancellationToken).ConfigureAwait(false)
            ?? new EncoreSearchResponse();

        await PopulateSongsAsync(payload.Data, payload.Found, payload.Page, cancellationToken).ConfigureAwait(false);
        return DataItems!;
    }

    public async Task<ObservableCollection<EncoreSong>> AdvancedSearchAsync(
        bool reset,
        EncoreAdvancedSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (reset)
        {
            CurrentPage = 1;
            HasMoreRecords = true;
            DataItems ??= [];
            DataItems.Clear();
        }

        request.Page = CurrentPage;
        request.PerPage = RecordsPerPage;

        var response = await _httpClient.PostAsJsonAsync("/search/advanced", request, JsonCerealOptions.Instance, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<EncoreAdvancedSearchResponse>(JsonCerealOptions.Instance, cancellationToken).ConfigureAwait(false)
            ?? new EncoreAdvancedSearchResponse();

        await PopulateSongsAsync(payload.Data, payload.Found, CurrentPage, cancellationToken).ConfigureAwait(false);
        return DataItems!;
    }

    public string GetDownloadUrl(string md5, bool noVideo = false)
    {
        return string.IsNullOrWhiteSpace(md5)
            ? string.Empty
            : $"{FilesBaseUrl}/{md5}{(noVideo ? "_novideo" : string.Empty)}.sng";
    }

    public string GetAlbumArtUrl(string? albumArtMd5)
    {
        return string.IsNullOrWhiteSpace(albumArtMd5)
            ? "avares://ChartHub/Resources/Images/noalbumart.png"
            : $"{FilesBaseUrl}/{albumArtMd5}.jpg";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task PopulateSongsAsync(
        IEnumerable<EncoreSongDto> songs,
        int found,
        int page,
        CancellationToken cancellationToken)
    {
        DataItems ??= [];

        var mapped = songs.Select(MapSong).ToList();
        var membership = await _libraryCatalog.GetMembershipMapAsync(
            LibrarySourceNames.Encore,
            mapped.SelectMany(song => song.GetCatalogSourceIds()),
            cancellationToken).ConfigureAwait(false);

        foreach (var song in mapped)
        {
            song.IsInLibrary = song.GetCatalogSourceIds()
                .Any(sourceId => membership.TryGetValue(sourceId, out var isPresent) && isPresent);
            DataItems.Add(song);
        }

        TotalResults = found;
        CurrentPage = page;
        HasMoreRecords = DataItems.Count < found;
    }

    private EncoreSong MapSong(EncoreSongDto song)
    {
        var sourceId = song.ChartId > 0 ? song.ChartId.ToString() : song.Md5;
        var charter = !string.IsNullOrWhiteSpace(song.Charter)
            ? song.Charter
            : (!string.IsNullOrWhiteSpace(song.ApplicationUsername) ? song.ApplicationUsername : "Unknown Charter");

        return new EncoreSong
        {
            ChartId = song.ChartId,
            SongId = song.SongId,
            GroupId = song.GroupId,
            Md5 = song.Md5,
            ChartHash = song.ChartHash ?? string.Empty,
            VersionGroupId = song.VersionGroupId,
            ApplicationUsername = song.ApplicationUsername ?? string.Empty,
            Name = song.Name ?? "Unknown Song",
            Artist = song.Artist ?? "Unknown Artist",
            Album = song.Album ?? "Unknown Album",
            Genre = song.Genre ?? "Unknown Genre",
            Year = song.Year ?? string.Empty,
            Charter = charter,
            AlbumArtUrl = GetAlbumArtUrl(song.AlbumArtMd5),
            DownloadUrl = GetDownloadUrl(song.Md5),
            SongLengthMs = song.SongLength,
            PreviewStartTimeMs = song.PreviewStartTime,
            FormattedTime = Toolbox.ConvertMillisecondsToText(song.SongLength),
            BandDifficulty = song.DiffBand,
            GuitarDifficulty = song.DiffGuitar,
            GuitarCoopDifficulty = song.DiffGuitarCoop,
            RhythmDifficulty = song.DiffRhythm,
            DrumsDifficulty = song.DiffDrums,
            RealDrumsDifficulty = song.DiffDrumsReal,
            BassDifficulty = song.DiffBass,
            GuitarGhlDifficulty = song.DiffGuitarGhl,
            BassGhlDifficulty = song.DiffBassGhl,
            VocalsDifficulty = song.DiffVocals,
            KeysDifficulty = song.DiffKeys,
            HasVideoBackground = song.HasVideoBackground,
            Modchart = song.Modchart,
            SourceName = LibrarySourceNames.Encore,
            SourceId = sourceId,
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}