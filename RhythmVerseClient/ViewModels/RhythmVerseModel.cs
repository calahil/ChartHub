using CommunityToolkit.Mvvm.Input;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Maui.Controls;
using RhythmVerseClient.Api;
using RhythmVerseClient.Services;
using RhythmVerseClient.Strings;
using RhythmVerseClient.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace RhythmVerseClient.ViewModels
{
    public class ViewSong : INotifyPropertyChanged
    {
        private string artist;
        private string title;
        private long? downloads;
        private string author;
        private string? avatar;
        private string? albumArt;
        private long? songLength;
        private string downloadLink;
        private string fileName;
        private long fileSize;
        private string album;
        private string? formattedTme;
        private string? gameformat;

        public string Artist
        {
            get => artist;
            set
            {
                artist = value;
                OnPropertyChanged();
            }
        }

        public string Title
        {
            get => title;
            set
            {
                title = value;
                OnPropertyChanged();
            }
        }

        public string Album
        {
            get => album;
            set
            {
                album = value;
                OnPropertyChanged();
            }
        }

        public long? Downloads
        {
            get => downloads;
            set
            {
                downloads = value;
                OnPropertyChanged();
            }
        }

        public string Author
        {
            get => author;
            set
            {
                author = value;
                OnPropertyChanged();
            }
        }

        public string? Avatar
        {
            get => avatar;
            set
            {
                avatar = value;
                OnPropertyChanged();
            }
        }

        public string? AlbumArt
        {
            get => albumArt;
            set
            {
                albumArt = value;
                OnPropertyChanged();
            }
        }

        public long? SongLength
        {
            get => songLength;
            set
            {
                songLength = value;
                OnPropertyChanged();
            }
        }

        public string DownloadLink
        {
            get => downloadLink;
            set
            {
                downloadLink = value;
                OnPropertyChanged();
            }
        }

        public string FileName
        {
            get => fileName;
            set
            {
                fileName = value;
                OnPropertyChanged();
            }
        }

        public long FileSize
        {
            get => fileSize;
            set
            {
                fileSize = value;
                OnPropertyChanged();
            }
        }

        public string? FormattedTme
        {
            get => formattedTme;
            set
            {
                formattedTme = value;
                OnPropertyChanged();
            }
        }

        public string? Gameformat
        {
            get => gameformat;
            set
            {
                gameformat = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class InstrumentItem
    {
        public string DisplayName { get; set; }
        public string Value { get; set; }
    }



    public class RhythmVerseModel : INotifyPropertyChanged
    {
        private readonly AppGlobalSettings globalSettings;
        private RhythmVerseApiClient apiClient;
        private DownloadService downloadService;
        private long? RecordsPerPage = 25;
        private bool _hasMoreRecords = true;
        private const string BaseUrl = "https://rhythmverse.co";
        private Dictionary<string, List<string>> dictionary;

        public List<string> Filters { get; } = new List<string> { "Artist", "Downloads", "Song Length", "Title" };
        public List<string> Orders { get; } = new List<string> { "Ascending", "Descending" };

        public ObservableCollection<InstrumentItem> Instruments { get; set; }

        private InstrumentItem _selectedInstrument;
        public InstrumentItem SelectedInstrument
        {
            get => _selectedInstrument;
            set
            {
                _selectedInstrument = value;
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
        public long? EndRecord
        {
            get => _endRecord;
            set
            {
                _endRecord = value;
                OnPropertyChanged();
            }
        }

        public bool IsPlaceholder
        {
            get => isPlaceholder;
            set
            {
                isPlaceholder = value;
                OnPropertyChanged();
            }
        }
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

        private bool _isLoading = false;
        private bool isPlaceholder;

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        public string SearchText { get; set; } = string.Empty;
        public IAsyncRelayCommand SearchButtonCommand { get; }
        public IAsyncRelayCommand DownloadFileCommand { get; }
        public IAsyncRelayCommand ThresholdReachedCommand { get; }
        public RhythmVersePageStrings PageStrings { get; }

        public RhythmVerseModel(AppGlobalSettings settings, IConfiguration configuration)
        {
            globalSettings = settings;
            PageStrings = new RhythmVersePageStrings();
            apiClient = new RhythmVerseApiClient(configuration);
            _dataItems = [];
            _downloads = [];
            _selectedFilter = "Artist";
            _selectedOrder = "Ascending";
            _currentPage = 1;
            IsPlaceholder = true;
            SearchButtonCommand = new AsyncRelayCommand(SearchButton);
            DownloadFileCommand = new AsyncRelayCommand(DownloadFile);
            ThresholdReachedCommand = new AsyncRelayCommand(ThresholdReached);
            downloadService = new DownloadService(configuration);

            dictionary = new Dictionary<string, List<string>>
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
            SelectedInstrument = Instruments[0];
        }

        /*public void SortDataItems()
        {
            if (DataItems == null || DataItems.Count == 0) return;

            IEnumerable<ViewSong> sortedData;

            switch (SelectedFilter.Filter.ToLower())
            {
                case "artist":
                    sortedData = DataItems.OrderBy(s => s.Artist).ToList(); // Force execution here
                    break;
                case "title":
                    sortedData = DataItems.OrderBy(s => s.Title).ToList(); // Force execution here
                    break;
                case "downloads":
                    sortedData = DataItems.OrderBy(s => s.Downloads ?? 0).ToList(); // Force execution here
                    break;
                case "songlength":
                    sortedData = DataItems.OrderBy(s => s.SongLength ?? 0).ToList(); // Force execution here
                    break;
                default:
                    sortedData = DataItems.OrderBy(s => s.Title).ToList(); // Default sort and force execution
                    break;
            }

            DataItems.Clear();
            foreach (var song in sortedData)
            {
                DataItems.Add(song);
            }
        }*/

        public async Task SearchButton()
        {
            if (DataItems != null)
            {
                DataItems.Clear();
            }
            CurrentPage = 1;
            IsLoading = false;
            IsPlaceholder = true;
            _hasMoreRecords = true;
            await LoadDataAsync();
        }

        public async Task DownloadFile()
        {
            if (SelectedFile == null)
                return;


            var downloadFile = new DownloadFile(SelectedFile.FileName, globalSettings.StagingDir, SelectedFile.DownloadLink, SelectedFile.FileSize);
            Downloads.Add(downloadFile);
            await downloadService.DownloadFileAsync(downloadFile);

            File.Move(Toolbox.ConstructPath(downloadFile.FilePath, downloadFile.DisplayName), Toolbox.ConstructPath(globalSettings.DownloadDir, downloadFile.DisplayName), true);
        }

        public async Task ThresholdReached()
        {
            //await LoadDataAsync();
        }

        public async Task LoadDataAsync()
        {
            if (IsLoading) return;

            IsLoading = true;

            if (!_hasMoreRecords) return;


            if (string.IsNullOrEmpty(SelectedFilter) || string.IsNullOrEmpty(SelectedOrder))
            {
                SelectedFilter = "Artist";
                SelectedOrder = "Ascending";
            }
            var filter = ConvertFilter(SelectedFilter);
            var order = GetSortOrder(filter, SelectedOrder);
            var instrument = SelectedInstrument.Value ?? string.Empty;
            var response = await apiClient.GetSongFilesAsync(_currentPage, RecordsPerPage, SearchText.ToLower(), filter, order, instrument);


            if (response != null && response.Data != null)
            {
                if (response.Data.Records.TotalFiltered != null)
                {

                    TotalResults = response.Data.Records.TotalFiltered;
                    TotalPages = TotalResults / RecordsPerPage;
                    if ((TotalResults % RecordsPerPage) > 0)
                        TotalPages++;
                }

                if (response.Data.Pagination.Start + 1 > 0)
                {
                    StartRecord = response.Data.Pagination.Start + 1;
                    EndRecord = StartRecord + RecordsPerPage - 1;
                }

                if (DataItems == null)
                    DataItems = [];
                else
                    DataItems.Clear();
                foreach (var song in response.Data.Songs)
                {
                    if (!song.File.DownloadUrl.StartsWith("http://marketplace.xbox.com") && !song.File.DownloadUrl.StartsWith("none"))
                    {
                        var songView = new ViewSong();
                        songView.Artist = song.File.FileArtist ?? song.File.FileName ?? song.File.Filename ?? "Unknown";
                        songView.Title = song.File.FileTitle ?? song.File.FileName ?? song.File.Filename ?? "Unknown";
                        songView.Album = song.File.FileAlbum ?? song.File.FileName ?? song.File.Filename ?? "Unknown";
                        var image = song.File.AlbumArt ?? song.Data.AlbumArt;
                        if (image != null)
                        {
                            if (!image.StartsWith(BaseUrl))
                            {
                                image = BaseUrl + image;
                            }
                            songView.AlbumArt = image;
                        }

                        songView.Downloads = song.File.Downloads != 0 ? song.File.Downloads : song.Data.Downloads;
                        songView.FileName = song.File.FileName ?? song.File.Filename ?? "missing";
                        songView.SongLength = (song.File.FileSongLength ?? 0) != 0 ? song.File.FileSongLength.Value : song.Data.SongLength ?? 0;
                        songView.FileSize = song.File.Size;
                        songView.FormattedTme = ConvertSecondstoText(songView.SongLength);
                        Author author = song.File.Author ?? new Author();
                        songView.Author = author.Name;

                        var avatarPath = author.AvatarPath;
                        if (avatarPath != null)
                        {
                            if (!avatarPath.StartsWith("http"))
                            {
                                avatarPath = BaseUrl + avatarPath;
                            }

                            songView.Avatar = avatarPath;
                        }
                        else
                        {
                            songView.Avatar = "blankprofile.png";
                        }

                        if (!song.File.DownloadUrl.StartsWith("http"))
                        {
                            songView.DownloadLink = BaseUrl + song.File.DownloadUrl;
                        }
                        else
                        {
                            songView.DownloadLink = song.File.DownloadUrl;
                        }

                        if (dictionary.TryGetValue(song.File.Gameformat, out List<string> value))
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
            else
            {

                _hasMoreRecords = false;
            }

            IsLoading = false;
            IsPlaceholder = false;
        }

        private string ConvertFilter(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }
            return input.Replace("Song ", "").ToLower();
        }

        private string ConvertSecondstoText(long? input)
        {
            if (input != null)
            {
                long? minutes = input / 60;
                int seconds = (int)input % 60;


                return $"{minutes}:{seconds:D2}";
            }
            else
            {
                return "00:00";
            }
        }

        private string GetSortOrder(string filter, string order)
        {
            bool isStringField = filter.Equals("Artist", StringComparison.OrdinalIgnoreCase) ||
                                 filter.Equals("Title", StringComparison.OrdinalIgnoreCase);

            // Adjust order based on the type of data
            if (isStringField)
            {
                return order == "Ascending" ? "ASC" : "DESC";
            }
            else // Assume numerical data for other fields
            {
                return order == "Ascending" ? "DESC" : "ASC";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
