using CommunityToolkit.Mvvm.Input;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
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
        private ImageSource avatar;
        private ImageSource albumArt;
        private long? songLength;
        private string downloadLink;
        private string fileName;
        private long fileSize;

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

        public ImageSource Avatar
        {
            get => avatar;
            set
            {
                avatar = value;
                OnPropertyChanged();
            }
        }

        public ImageSource AlbumArt
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


        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RhythmVerseModel : INotifyPropertyChanged
    {
        private readonly AppGlobalSettings globalSettings;
        private RhythmVerseApiClient apiClient;
        private DownloadService downloadService;
        private int _currentPage = 1;
        private const int RecordsPerPage = 25;
        private bool _isLoading = false;
        private bool _hasMoreRecords = true;
        private const string BaseUrl = "https://rhythmverse.co";

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
        public List<string> SortFilters { get; set; } = ["Artist", "Title", "Downloads", "Song Length"];
        public string SelectedFilter { get; set; } = "Artist";
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
            SearchButtonCommand = new AsyncRelayCommand(SearchButton);
            DownloadFileCommand = new AsyncRelayCommand(DownloadFile);
            ThresholdReachedCommand = new AsyncRelayCommand(ThresholdReached);
            downloadService = new DownloadService(configuration);
        }

        public void SortDataItems()
        {
            if (DataItems == null || DataItems.Count == 0) return;

            IOrderedEnumerable<ViewSong> sortedData;

            switch (SelectedFilter.ToLower())
            {
                case "artist":
                    sortedData = true
                        ? DataItems.OrderBy(s => s.Artist)
                        : DataItems.OrderByDescending(s => s.Artist);
                    break;

                case "title":
                    sortedData = true
                        ? DataItems.OrderBy(s => s.Title)
                        : DataItems.OrderByDescending(s => s.Title);
                    break;

                case "downloads":
                    sortedData = true
                        ? DataItems.OrderBy(s => s.Downloads ?? 0)
                        : DataItems.OrderByDescending(s => s.Downloads ?? 0);
                    break;

                case "songlength":
                    sortedData = true
                        ? DataItems.OrderBy(s => s.SongLength ?? 0)
                        : DataItems.OrderByDescending(s => s.SongLength ?? 0);
                    break;

                default:
                    sortedData = DataItems.OrderBy(s => s.Title); // Default sort
                    break;
            }

            // Clear and repopulate the collection with the sorted data
            DataItems.Clear();
            foreach (var song in sortedData)
            {
                DataItems.Add(song);
            }
        }

        public async Task SearchButton()
        {
            if (DataItems != null)
            {
                DataItems.Clear();
            }
            _currentPage = 1;
            _isLoading = false;
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

            System.IO.File.Move(Toolbox.ConstructPath(downloadFile.FilePath, downloadFile.DisplayName), Toolbox.ConstructPath(globalSettings.DownloadDir, downloadFile.DisplayName));
        }

        public async Task ThresholdReached()
        {
            await LoadDataAsync();
        }

        public async Task LoadDataAsync()
        {
            if (_isLoading) return;

            _isLoading = true;

            if (!_hasMoreRecords) return;

            var response = await apiClient.GetSongFilesAsync(_currentPage, RecordsPerPage, ConvertSpacesToPlus(SearchText), SelectedFilter.ToLower());


            if (response != null && response.Data != null)
            {
                if (DataItems == null)
                    DataItems = [];

                foreach (var song in response.Data.Songs)
                {
                    var songView = new ViewSong();
                    songView.Artist = song.Data.Artist ?? song.File.FileName ?? song.File.Filename ?? "Unknown";
                    songView.Title = song.Data.Title ?? song.File.FileName ?? song.File.Filename ?? "Unknown";
                    var image = song.Data.AlbumArt ?? song.File.AlbumArt ?? "noalbumart.png";
                    songView.Downloads = song.Data.Downloads ?? song.File.Downloads ?? 0;
                    songView.FileName = song.File.FileName ?? song.File.Filename ?? null;
                    songView.SongLength = song.Data.SongLength ?? song.File.FileSongLength ?? 0;
                    songView.FileSize = song.File.Size;

                    Author author = song.File.Author ?? new Author();
                    songView.Author = author.Name;

                    if (author.AvatarPath != null)
                    {
                        var avatarPath = author.AvatarPath;
                        if (!avatarPath.StartsWith("http"))
                        {
                            author.AvatarPath = BaseUrl + avatarPath;
                        }

                        songView.Avatar = ImageSource.FromUri(new Uri(author.AvatarPath));
                    }
                    else
                    {
                        songView.Avatar = ImageSource.FromFile("blankprofile.png");
                    }

                    if (song.Data.AlbumArt != string.Empty && song.Data.AlbumArt != null)
                    {
                        var albumArt = song.Data.AlbumArt;
                        if (!albumArt.StartsWith(BaseUrl))
                        {
                            albumArt = BaseUrl + albumArt;
                        }
                        songView.AlbumArt = ImageSource.FromUri(new Uri(albumArt));
                    }
                    else if(song.File.AlbumArt != string.Empty && song.File.AlbumArt != null)
                    {
                        var albumArt = song.File.AlbumArt;
                        if (!albumArt.StartsWith(BaseUrl))
                        {
                            albumArt = BaseUrl + albumArt;
                        }
                        songView.AlbumArt = ImageSource.FromUri(new Uri(albumArt));
                    }
                    else
                    {
                        songView.AlbumArt = ImageSource.FromFile("noalbumart.png");
                    }

                    if (!song.File.DownloadUrl.StartsWith("http"))
                    {
                        songView.DownloadLink = BaseUrl + song.File.DownloadUrl;
                    }
                    else
                    {
                        songView.DownloadLink = song.File.DownloadUrl;
                    }

                    if (!DataItems.Contains(songView))
                    {
                        DataItems.Add(songView);
                    }
                }
                _currentPage++;
            }
            else
            {
                _hasMoreRecords = false;
            }

            _isLoading = false;
        }

        private string ConvertSpacesToPlus(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            return input.ToLower();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
