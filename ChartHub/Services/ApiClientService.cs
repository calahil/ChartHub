
using Avalonia.Platform;
using Microsoft.Extensions.Configuration;
using ChartHub.Models;
using ChartHub.Utilities;
using ChartHub.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;

namespace ChartHub.Services
{
    public class ApiClientService : INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly Func<string?> _loadEmbeddedMockData;
        private readonly Func<string?> _resolveMockDataPath;
        private readonly Func<bool> _isAndroid;
        private readonly Dictionary<string, List<string>> dictionary = new()
        {
                { "rb3", ["Rock Band 3", "rb3.png"] },
                { "rv", ["RhythmVerse", "rv.png"] },
                { "rb3xbox", ["Rock Band 3 Xbox 360", "rb3xbox.png"] },
                { "rb3wii", ["Rock Band 3 Wii", "rb3wii.png"] },
                { "rb3ps3", ["Rock Band 3 PS3", "rb3ps3.png"] },
                { "wtde", ["Guitar Hero World Tour: Definitive Edition", "wtde.png"] },
                { "tbrbxbox", ["The Beatles: Rock Band XBox 360", "tbrb.png"] },
                { "tbrbps3", ["The Beatles: Rock Band PS3", "tbrb.png"] },
                { "tbrb", ["The Beatles: Rock Band", "tbrb.png"] },
                { "yarg", ["YARG", "yarg.png"] },
                { "rb2xbox", ["Rock Band 2 Xbox 360", "rb2.png"] },
                { "ps", ["Phase Shift", "ps.png"] },
                { "chm", ["Clone Hero", "ch.png"] },
                { "ch", ["Clone Hero", "ch.png"] },
                { "gh3pc", ["Guitar Hero World Tour PC", "gh.png"] }
            };

        private const string BaseUrl = "https://rhythmverse.co";

        private readonly List<string> Ratings = new()
        {
            "\uebb5\uebb5\uebb5\uebb5\uebb5",
            "\u2B24\uebb5\uebb5\uebb5\uebb5",
            "\u2B24\u2B24\uebb5\uebb5\uebb5",
            "\u2B24\u2B24\u2B24\uebb5\uebb5",
            "\u2B24\u2B24\u2B24\u2B24\uebb5",
        };

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
        public long? RecordsPerPage { get; } = 25;


        private bool _hasMoreRecords = true;
        public bool HasMoreRecords
        {
            get => _hasMoreRecords;
            set
            {
                _hasMoreRecords = value;
                OnPropertyChanged();
            }
        }

        private long? _totalPages;
        public long? TotalPages
        {
            get => _totalPages;
            set
            {
                _totalPages = value;
                OnPropertyChanged();
            }
        }

        private long? _totalResults;
        public long? TotalResults
        {
            get => _totalResults;
            set
            {
                _totalResults = value;
                OnPropertyChanged();
            }
        }

        private long? _currentPage;
        public long? CurrentPage
        {
            get => _currentPage;
            set
            {
                _currentPage = value;
                OnPropertyChanged();
            }
        }

        private long? _startRecord;
        public long? StartRecord
        {
            get => _startRecord;
            set
            {
                _startRecord = value;
                OnPropertyChanged();
            }
        }

        private long? _endRecord;
        private string? ResponseDebug;

        public long? EndRecord
        {
            get => _endRecord;
            set
            {
                _endRecord = value;
                OnPropertyChanged();
            }
        }

        public ApiClientService(IConfiguration configuration)
            : this(
                configuration,
                CreateHttpClient(configuration),
                LoadMockDataFromEmbeddedResource,
                ResolveMockDataPath,
                () => OperatingSystem.IsAndroid())
        {
        }

        internal ApiClientService(
            IConfiguration configuration,
            HttpClient httpClient,
            Func<string?> loadEmbeddedMockData,
            Func<string?> resolveMockDataPath,
            Func<bool> isAndroid)
        {
            _configuration = configuration;
            _httpClient = httpClient;
            _loadEmbeddedMockData = loadEmbeddedMockData;
            _resolveMockDataPath = resolveMockDataPath;
            _isAndroid = isAndroid;

            if (_httpClient.BaseAddress is null)
            {
                _httpClient.BaseAddress = new Uri(BaseUrl);
            }

            if (_httpClient.DefaultRequestHeaders.Authorization is null)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", configuration["rhythmverseToken"]);
            }

            DataItems = new ObservableCollection<ViewSong>();
            _currentPage = 1;
        }

        public async Task<ObservableCollection<ViewSong>> GetSongFilesAsync(bool search, string searchString, string sort, string order, List<InstrumentItem> instrument, string authorText)
        {
            if (search)
            {
                CurrentPage = 1;
                HasMoreRecords = true;
            }
            try
            {
                string endpoint;
                //string payload;

                if (!string.IsNullOrEmpty(searchString))
                {
                    endpoint = "api/all/songfiles/search/live";
                }
                else
                {
                    endpoint = "api/all/songfiles/list";
                }

                try
                {

                    if (DataItems == null)
                        DataItems = [];
                    else if (search)
                        DataItems.Clear();


                    var collection = new List<KeyValuePair<string, string>>();
                    foreach (var item in instrument)
                    {

                        if (!string.IsNullOrEmpty(item.Value))
                        {
                            collection.Add(new("instrument", $"{item.Value}"));
                        }
                    }
                    if (!string.IsNullOrEmpty(authorText))
                    {
                        collection.Add(new("author", $"{authorText}"));
                    }
                    collection.Add(new("sort[0][sort_by]", $"{sort}"));
                    collection.Add(new("sort[0][sort_order]", $"{order}"));
                    collection.Add(new("data_type", "full"));
                    if (!string.IsNullOrEmpty(searchString))
                    {
                        collection.Add(new("text", $"{searchString}"));
                    }
                    collection.Add(new("page", $"{CurrentPage}"));
                    collection.Add(new("records", $"{RecordsPerPage}"));
                    var content = new FormUrlEncodedContent(collection);

                    string responseBody;

                    var useMockData = IsMockDataEnabled(_configuration);

                    if (useMockData)
                    {
                        responseBody = _loadEmbeddedMockData() ?? string.Empty;

                        if (string.IsNullOrEmpty(responseBody))
                        {
                            var mockDataPath = _resolveMockDataPath();
                            if (mockDataPath != null)
                            {
                                responseBody = await File.ReadAllTextAsync(mockDataPath);
                            }
                        }

                        if (string.IsNullOrEmpty(responseBody))
                        {
                            Logger.LogInfo("Api", "Mock data file was not found; falling back to live API");
                            HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content);
                            response.EnsureSuccessStatusCode();
                            responseBody = await response.Content.ReadAsStringAsync();
                        }
                    }
                    else
                    {
                        HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content);
                        response.EnsureSuccessStatusCode();
                        responseBody = await response.Content.ReadAsStringAsync();
                    }

                    ResponseDebug = responseBody;
                    var DecodedResponse = RootResponse.FromJson(responseBody);

                    if (DecodedResponse != null && DecodedResponse.Data != null)
                    {
                        TotalResults = DecodedResponse.Data.Records.TotalFiltered;
                        TotalPages = TotalResults / RecordsPerPage;
                        if ((TotalResults % RecordsPerPage) > 0)
                            TotalPages++;


                        if (DecodedResponse.Data.Pagination.Start + 1 > 0)
                        {
                            StartRecord = DecodedResponse.Data.Pagination.Start + 1;
                            EndRecord = StartRecord + RecordsPerPage - 1;
                        }

                        foreach (var song in DecodedResponse.Data.Songs)
                        {
                            if (song != null)
                            {
                                var songFile = song.File;
                                if (songFile is null)
                                    continue;

                                var downloadUrl = songFile.DownloadUrl ?? string.Empty;
                                if (!downloadUrl.StartsWith("http://marketplace.xbox.com") && !downloadUrl.StartsWith("https://store.xbox.com/"))
                                {
                                    var songView = new ViewSong();
                                    var songData = song.Data.DataData;

                                    if (songData != null)
                                    {
                                        songView.Artist = songFile.FileArtist as string ?? songData.Artist ?? songFile.Filename ?? "Unknown";
                                        songView.Title = songFile.FileTitle as string ?? songData.Title ?? songFile.Filename ?? "Unknown";
                                        songView.Album = songFile.FileAlbum as string ?? songData.Album ?? songFile.Filename ?? "Unknown";
                                        songView.Downloads = songFile.Downloads != 0 ? songFile.Downloads : songData.Downloads;
                                        songView.Comments = songFile.Comments ?? 0;

                                        if (songData.SongLength > 0)
                                        {
                                            songView.SongLength = songData.SongLength;
                                        }
                                        else
                                        {
                                            songView.SongLength = songFile.SongLength;
                                        }
                                        songView.Genre = songData.Genre ?? songFile.FileGenre ?? "Music";
                                        songView.Year = songData.Year.ToString() ?? songFile.FileYear.ToString() ?? "1955";
                                    }
                                    else
                                    {
                                        songView.Artist = songFile.FileArtist as string ?? songFile.Filename ?? "Unknown";
                                        songView.Title = songFile.FileTitle as string ?? songFile.Filename ?? "Unknown";
                                        songView.Album = songFile.FileAlbum as string ?? songFile.Filename ?? "Unknown";
                                        songView.Downloads = songFile.Downloads != 0 ? songFile.Downloads : 0;
                                        songView.Comments = songFile.Comments ?? 0;
                                        songView.Year = songFile.FileYear.ToString() ?? "1955";
                                        songView.Genre = songFile.FileGenre ?? "Music";

                                        if (songFile.SongLength > 0)
                                        {
                                            songView.SongLength = songFile.SongLength;
                                        }
                                        else
                                        {
                                            songView.SongLength = songFile.FileSongLength as long?;
                                        }
                                    }
                                    songView.FileName = songFile.FileName ?? songFile.Filename ?? "missing";
                                    songView.FileSize = songFile.Size;

                                    string? apiAlbumArt = songData?.AlbumArt.String;
                                    string? fileAlbumArt = songFile.AlbumArt;
                                    var image = !string.IsNullOrWhiteSpace(apiAlbumArt)
                                        ? apiAlbumArt
                                        : (!string.IsNullOrWhiteSpace(fileAlbumArt) ? fileAlbumArt : null);

                                    if (image != null && !string.IsNullOrWhiteSpace(image))
                                    {
                                        if (!image.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                            && !image.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                                        {
                                            image = BaseUrl + image;
                                        }
                                        songView.AlbumArt = image;
                                    }
                                    else
                                    {
                                        songView.AlbumArt = "avares://ChartHub/Resources/Images/noalbumart.png";
                                    }

                                    songView.FormattedTime = Toolbox.ConvertSecondstoText(songView.SongLength);

                                    Author author = song.File.Author ?? new Author();

                                    var avatarPath = author.AvatarPath;
                                    if (avatarPath != null)
                                    {
                                        if (!avatarPath.StartsWith("http"))
                                        {
                                            avatarPath = BaseUrl + avatarPath;
                                        }

                                        author.AvatarPath = avatarPath;
                                    }
                                    else
                                    {
                                        author.AvatarPath = "avares://ChartHub/Resources/Images/blankprofile.png";
                                    }

                                    songView.Author = author;

                                    if (!downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                    {
                                        songView.DownloadLink = BaseUrl + downloadUrl;
                                    }
                                    else
                                    {
                                        songView.DownloadLink = downloadUrl;
                                    }

                                    songView.SourceName = LibrarySourceNames.RhythmVerse;
                                    songView.SourceId = !string.IsNullOrWhiteSpace(songFile.FileId)
                                        ? songFile.FileId
                                        : (!string.IsNullOrWhiteSpace(songFile.FileName)
                                            ? songFile.FileName
                                            : songView.DownloadLink);

                                    songView.DrumString = GiveMeRatingsNow(song, "drums");
                                    songView.GuitarString = GiveMeRatingsNow(song, "guitar");
                                    songView.BassString = GiveMeRatingsNow(song, "bass");
                                    songView.VocalString = GiveMeRatingsNow(song, "vocals");
                                    songView.KeysString = GiveMeRatingsNow(song, "keys");

                                    var gameFormat = song.File.Gameformat ?? string.Empty;
                                    if (dictionary.TryGetValue(gameFormat, out List<string>? value))
                                    {
                                        songView.Gameformat = value[1];
                                    }

                                    if (!DataItems.Contains(songView))
                                    {
                                        DataItems.Add(songView);
                                    }
                                }
                            }
                        }

                        HasMoreRecords = CurrentPage < TotalPages;

                    }
                    else
                    {
                        HasMoreRecords = false;
                    }
                    return DataItems;
                }

                catch (HttpRequestException e)
                {
                    Logger.LogError("Api", "Request error while loading song data", e);
                    return [];
                }
            }


            catch (Exception ex)
            {
                Logger.LogError("Api", "Unexpected error while loading song data", ex);
                return [];
            }
        }

        private static HttpClient CreateHttpClient(IConfiguration configuration)
        {
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(BaseUrl)
            };
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", configuration["rhythmverseToken"]);
            return httpClient;
        }
        private static bool IsMockDataEnabled(IConfiguration configuration)
        {
            var candidates = new[]
            {
                configuration["Runtime:UseMockData"],
                configuration["UseMockData"],
            };

            foreach (var candidate in candidates)
            {
                if (bool.TryParse(candidate, out var enabled) && enabled)
                    return true;

                if (string.Equals(candidate, "1", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
        private static string? ResolveMockDataPath()
        {
            // Support launches from IDE and from built output directories.
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Tests", "test.json"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ChartHub", "Tests", "test.json")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "ChartHub", "Tests", "test.json")),
                Path.Combine(Directory.GetCurrentDirectory(), "ChartHub", "Tests", "test.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Tests", "test.json")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string? LoadMockDataFromEmbeddedResource()
        {
            try
            {
                var uri = new Uri("avares://ChartHub/Tests/test.json");
                if (!AssetLoader.Exists(uri))
                {
                    return null;
                }

                using var stream = AssetLoader.Open(uri);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                Logger.LogError("Api", "Failed to load mock data from embedded resource", ex);
                return null;
            }
        }

        private int GiveMeRatingsNow(Song song, string instrument)
        {
            int dataRating;
            int fileRating;
            var data = song.Data.DataData;
            var file = song.File;

            switch (instrument)
            {
                case "drums":
                    if (data != null)
                    {
                        if (data.DiffDrums.HasValue &&
                                             data.DiffDrums.Value >= 0 &&
                                             data.DiffDrums.Value < Ratings.Count)
                        {
                            dataRating = (int)data.DiffDrums.Value;
                        }
                        else
                        {
                            dataRating = 0;
                        }
                    }
                    else
                    {
                        dataRating = 0;
                    }

                    if (file.DiffDrums.HasValue &&
                        file.DiffDrums.Value >= 0 &&
                        file.DiffDrums.Value < Ratings.Count)
                    {
                        fileRating = (int)file.DiffDrums.Value;
                    }
                    else
                    {
                        fileRating = 0;
                    }

                    return Math.Max(dataRating, fileRating);
                case "guitar":
                    if (data != null)
                    {
                        if (data.DiffGuitar.HasValue &&
                                             data.DiffGuitar.Value >= 0 &&
                                             data.DiffGuitar.Value < Ratings.Count)
                        {
                            dataRating = (int)data.DiffGuitar.Value;
                        }
                        else
                        {
                            dataRating = 0;
                        }
                    }
                    else
                    {
                        dataRating = 0;
                    }

                    if (file.DiffGuitar.HasValue &&
                        file.DiffGuitar.Value >= 0 &&
                        file.DiffGuitar.Value < Ratings.Count)
                    {
                        fileRating = (int)file.DiffGuitar.Value;
                    }
                    else
                    {
                        fileRating = 0;
                    }

                    return Math.Max(dataRating, fileRating);
                case "bass":
                    if (data != null)
                    {
                        if (data.DiffBass.HasValue &&
                                             data.DiffBass.Value >= 0 &&
                                             data.DiffBass.Value < Ratings.Count)
                        {
                            dataRating = (int)data.DiffBass.Value;
                        }
                        else
                        {
                            dataRating = 0;
                        }
                    }
                    else
                    {
                        dataRating = 0;
                    }

                    if (file.DiffBass.HasValue &&
                        file.DiffBass.Value >= 0 &&
                        file.DiffBass.Value < Ratings.Count)
                    {
                        fileRating = (int)file.DiffBass.Value;
                    }
                    else
                    {
                        fileRating = 0;
                    }

                    return Math.Max(dataRating, fileRating);
                case "vocals":
                    if (data != null)
                    {
                        if (data.DiffVocals.HasValue &&
                                             data.DiffVocals.Value >= 0 &&
                                             data.DiffVocals.Value < Ratings.Count)
                        {
                            dataRating = (int)data.DiffVocals.Value;
                        }
                        else
                        {
                            dataRating = 0;
                        }
                    }
                    else
                    {
                        dataRating = 0;
                    }

                    if (file.DiffVocals.HasValue &&
                        file.DiffVocals.Value >= 0 &&
                        file.DiffVocals.Value < Ratings.Count)
                    {
                        fileRating = (int)file.DiffVocals.Value;
                    }
                    else
                    {
                        fileRating = 0;
                    }

                    return Math.Max(dataRating, fileRating);
                case "keys":
                    if (data != null)
                    {
                        if (data.DiffKeys.HasValue &&
                                             data.DiffKeys.Value >= 0 &&
                                             data.DiffKeys.Value < Ratings.Count)
                        {
                            dataRating = (int)data.DiffKeys.Value;
                        }
                        else
                        {
                            dataRating = 0;
                        }
                    }
                    else
                    {
                        dataRating = 0;
                    }

                    if (file.DiffKeys.HasValue &&
                        file.DiffKeys.Value >= 0 &&
                        file.DiffKeys.Value < Ratings.Count)
                    {
                        fileRating = (int)file.DiffKeys.Value;
                    }
                    else
                    {
                        fileRating = 0;
                    }

                    return Math.Max(dataRating, fileRating);
                default:
                    return 0;
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}