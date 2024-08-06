using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using RhythmVerseClient.Api;
using RhythmVerseClient.Models;
using RhythmVerseClient.Services;
using RhythmVerseClient.Strings;
using RhythmVerseClient.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RhythmVerseClient.ViewModels
{
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

    public class RhythmVerseModel : INotifyPropertyChanged
    {
        private readonly AppGlobalSettings globalSettings;
        private readonly DownloadService downloadService;

        private RhythmVerseApiClient _apiClient;
        public RhythmVerseApiClient ApiClient
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
        }

        public long? StartRecord
        {
            get { return ApiClient.StartRecord; }
        }

        public long? EndRecord
        {
            get { return ApiClient.EndRecord; }
        }

        private bool noResults;
        public bool NoResults
        {
            get => noResults;
            set
            {
                noResults = value;
                OnPropertyChanged();
            }
        }

        private bool _isLoading = false;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        private bool isPlaceholder;
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

        public List<string> Filters { get; } = ["Artist", "Downloads", "Song Length", "Title"];
        public List<string> Orders { get; } = ["Ascending", "Descending"];

        public ObservableCollection<InstrumentItem> Instruments { get; set; }

        public string SearchText { get; set; } = string.Empty;
        public IAsyncRelayCommand SearchButtonCommand { get; }
        public IAsyncRelayCommand DownloadFileCommand { get; }
        public RhythmVersePageStrings PageStrings { get; }

        public RhythmVerseModel(AppGlobalSettings settings, IConfiguration configuration)
        {
            globalSettings = settings;
            PageStrings = new RhythmVersePageStrings();
            _apiClient = new RhythmVerseApiClient(configuration);
            _dataItems = [];
            _downloads = [];
            _selectedFilter = "Artist";
            _selectedOrder = "Ascending";
            IsPlaceholder = true;
            NoResults = false;
            SearchButtonCommand = new AsyncRelayCommand(SearchButton);
            DownloadFileCommand = new AsyncRelayCommand(DownloadFile);
            downloadService = new DownloadService(configuration);
                     
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
            _selectedInstruments = [];
            _selectedInstruments.Add(Instruments[0]);

            _apiClient.PropertyChanged += ApiClient_PropertyChanged;
        }

        private void ApiClient_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Raise the PropertyChanged event for the corresponding property in the ViewModel
            OnPropertyChanged(e.PropertyName);
        }

        public async Task SearchButton()
        {
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
            await LoadDataAsync(true);
        }

        public async Task DownloadFile()
        {
            if (SelectedFile == null)
                return;

            var downloadFile = new DownloadFile(SelectedFile.FileName ?? string.Empty, globalSettings.StagingDir, SelectedFile.DownloadLink ?? string.Empty, SelectedFile.FileSize);
            Downloads.Add(downloadFile);
            await downloadService.DownloadFileAsync(downloadFile);

            File.Move(Toolbox.ConstructPath(downloadFile.FilePath, downloadFile.DisplayName), Toolbox.ConstructPath(globalSettings.DownloadDir, downloadFile.DisplayName), true);
        }

        public async Task LoadDataAsync(bool search)
        {
            if (IsLoading) return;

            IsLoading = true;

            if (!ApiClient.HasMoreRecords) return;


            if (string.IsNullOrEmpty(SelectedFilter) || string.IsNullOrEmpty(SelectedOrder))
            {
                SelectedFilter = "Artist";
                SelectedOrder = "Ascending";
            }
            var filter = Toolbox.ConvertFilter(SelectedFilter);
            var order = Toolbox.GetSortOrder(filter, SelectedOrder);
            var instrument = SelectedInstruments.ToList();
            DataItems = await ApiClient.GetSongFilesAsync(search, SearchText.ToLower(), filter, order, instrument);

            if (DataItems.Count < 1)
            {
                NoResults = true;
            }
            else
            {
                NoResults = false;
            }
            

            IsLoading = false;
            IsPlaceholder = false;
        }
    
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
