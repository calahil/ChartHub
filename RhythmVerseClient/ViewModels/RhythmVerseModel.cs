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
    public class RhythmVerseModel : INotifyPropertyChanged
    {
        private readonly AppGlobalSettings globalSettings;
        private RhythmVerseApiClient apiClient;
        private DownloadService downloadService;
        private int _currentPage = 1;
        private const int RecordsPerPage = 25;
        private bool _isLoading = false;
        private bool _hasMoreRecords = true;

        private ObservableCollection<Song>? _dataItems;
        public ObservableCollection<Song>? DataItems
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

        private Song? _selectedFile;
        public Song? SelectedFile
        {
            get => _selectedFile;
            set
            {
                _selectedFile = value;
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
            SearchButtonCommand = new AsyncRelayCommand(SearchButton);
            DownloadFileCommand = new AsyncRelayCommand(DownloadFile);
            ThresholdReachedCommand = new AsyncRelayCommand(ThresholdReached);

            downloadService = new DownloadService(configuration);
        }

        public async Task SearchButton()
        {
            if (DataItems != null)
            {
                DataItems.Clear();
            }
            _currentPage = 1;
            _isLoading = false;
            await LoadDataAsync();
        }

        public async Task DownloadFile()
        {
            if (SelectedFile == null)
                return;
            
            string downloadString;

            if (!SelectedFile.File.DownloadUrl.StartsWith("http"))
            {
                downloadString = "https://rhythmverse.co" + SelectedFile.File.DownloadUrl;
            }
            else
            {
                downloadString = SelectedFile.File.DownloadUrl;
            }

            var downloadFile = new DownloadFile(SelectedFile.File.FileName, globalSettings.StagingDir, downloadString, SelectedFile.File.Size);
            Downloads.Add(downloadFile);
            await downloadService.DownloadFileAsync(downloadFile);
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

            var response = await apiClient.GetSongFilesAsync(_currentPage, RecordsPerPage, ConvertSpacesToPlus(SearchText));


            if (response != null && response.Data.Songs != null)
            {
                if (DataItems == null)
                    DataItems = [];

                foreach (var song in response.Data.Songs)
                {
                    if (!DataItems.Contains(song))
                    {
                        DataItems.Add(song);
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
