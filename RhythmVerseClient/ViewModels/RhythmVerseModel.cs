using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using RhythmVerseClient.Models;
using RhythmVerseClient.Services;
using RhythmVerseClient.Strings;
using RhythmVerseClient.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

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

        private bool _isFilterPaneVisible;
        public bool IsFilterPaneVisible
        {
            get => _isFilterPaneVisible;
            set
            {
                if (_isFilterPaneVisible != value)
                {
                    _isFilterPaneVisible = value;
                    OnPropertyChanged();
                }
            }
        }

        private GridLength _filterPaneWidth;
        public GridLength FilterPaneWidth
        {
            get => _filterPaneWidth;
            set
            {
                if (_filterPaneWidth != value)
                {
                    _filterPaneWidth = value;
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

        private string _filterPaneButtonText;
        public string FilterPaneButtonText
        {
            get => _filterPaneButtonText;
            set
            {
                _filterPaneButtonText = value;
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


        public IAsyncRelayCommand RefreshButtonCommand { get; }
        public ICommand DownloadFileCommand { get; }
        public ICommand ToggleFilterPaneCommand { get; }

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
            _searchAuthorText = string.Empty;
            _searchText = string.Empty;
            _isAuthorFiltered = false;
            _isFilterPaneVisible = false;
            _filterPaneButtonText = GetFilterPaneText();
            _isLoading = false;
            _filterPaneWidth = new GridLength(0);
            IsPlaceholder = true;
            NoResults = false;
            RefreshButtonCommand = new AsyncRelayCommand(RefreshButton);
            DownloadFileCommand = new AsyncRelayCommand(DownloadFile);
            ToggleFilterPaneCommand = new RelayCommand(ToggleFilterPane);

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

        public void ToggleFilterPane()
        {
            IsFilterPaneVisible = !IsFilterPaneVisible;
            FilterPaneButtonText = GetFilterPaneText();
        }

        private string GetFilterPaneText()
        {
            if (IsFilterPaneVisible)
            {
                return PageStrings.HideFilter;
            }
            else
            {
                return PageStrings.ShowFilter;
            }
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
            IsLoading = false;
            IsPlaceholder = true;
            NoResults = false;
            await LoadDataAsync(string.IsNullOrEmpty(SearchText) || string.IsNullOrEmpty(SearchAuthorText));
        }

        public async Task DownloadFile()
        {
            if (SelectedFile == null)
                return;

            var downloadFile = new DownloadFile(SelectedFile.FileName ?? string.Empty, globalSettings.TempDir, SelectedFile.DownloadLink ?? string.Empty, SelectedFile.FileSize);
            Downloads.Add(downloadFile);
            await downloadService.DownloadFileAsync(downloadFile);

            File.Move(Path.Combine(downloadFile.FilePath, downloadFile.DisplayName), Path.Combine(globalSettings.DownloadDir, downloadFile.DisplayName), true);
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
            DataItems = await ApiClient.GetSongFilesAsync(search, SearchText.ToLower(), filter, order, instrument, SearchAuthorText);

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
