using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using RhythmVerseClient.Models;
using RhythmVerseClient.Services;
using RhythmVerseClient.Services.Transfers;
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

        private readonly ITransferOrchestrator _transferOrchestrator;

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
                    _ = Task.Run(() => LoadDataAsync(false));
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
        public IAsyncRelayCommand<ViewSong?> ViewCreatorCommand { get; }
        public IRelayCommand<DownloadFile?> CancelDownloadCommand { get; }

        private readonly Dictionary<DownloadFile, CancellationTokenSource> _downloadTokens = [];

        public RhythmVersePageStrings PageStrings { get; }

        public RhythmVerseViewModel(IConfiguration configuration, ITransferOrchestrator transferOrchestrator)
        {
            _transferOrchestrator = transferOrchestrator;
            PageStrings = new RhythmVersePageStrings();
            _apiClient = new ApiClientService(configuration);
            _dataItems = [];
            _downloads = [];
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
            DownloadFileCommand = new AsyncRelayCommand<ViewSong?>(DownloadFile);
            ViewCreatorCommand = new AsyncRelayCommand<ViewSong?>(ViewCreator);
            CancelDownloadCommand = new RelayCommand<DownloadFile?>(CancelDownload);
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
            _ = Task.Run(() => LoadDataAsync(false));
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
            IsLoading = false;
            IsPlaceholder = true;
            NoResults = false;
            await LoadDataAsync(string.IsNullOrEmpty(SearchText) || string.IsNullOrEmpty(SearchAuthorText));
        }

        public async Task DownloadFile(ViewSong? song)
        {
            var file = song ?? SelectedFile;
            if (file == null)
                return;

            var downloadItem = new DownloadFile(
                file.FileName ?? string.Empty,
                Path.GetTempPath(),
                file.DownloadLink ?? string.Empty,
                file.FileSize);

            var cts = new CancellationTokenSource();
            _downloadTokens[downloadItem] = cts;

            var result = await _transferOrchestrator.QueueSongDownloadAsync(file, downloadItem, Downloads, cts.Token);
            if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
                Logger.LogMessage($"Song transfer failed: {result.Error}");

            _downloadTokens.Remove(downloadItem);
            cts.Dispose();
        }

        private void CancelDownload(DownloadFile? downloadItem)
        {
            if (downloadItem is null)
                return;

            if (_downloadTokens.TryGetValue(downloadItem, out var cts))
            {
                downloadItem.Status = TransferStage.Cancelling.ToString();
                downloadItem.ErrorMessage = null;
                cts.Cancel();
            }
        }
        public async Task ViewCreator(ViewSong? song)
        {
            var file = song ?? SelectedFile;
            if (file == null || file.Author == null)
                return;

            SearchAuthorText = file.Author.Shortname;
            await LoadDataAsync(string.IsNullOrEmpty(SearchText) || string.IsNullOrEmpty(SearchAuthorText));
        }

        public async Task LoadDataAsync(bool search)
        {
            if (IsLoading) return;

            IsLoading = true;
            try
            {
                if (!ApiClient.HasMoreRecords)
                {
                    NoResults = DataItems == null || DataItems.Count < 1;
                    return;
                }

                if (string.IsNullOrEmpty(SelectedFilter) || string.IsNullOrEmpty(SelectedOrder))
                {
                    SelectedFilter = "Artist";
                    SelectedOrder = "Ascending";
                }
                var filter = Toolbox.ConvertFilter(SelectedFilter);
                var order = Toolbox.GetSortOrder(filter, SelectedOrder);
                var instrument = SelectedInstruments.ToList();
                DataItems = [];
                DataItems = await ApiClient.GetSongFilesAsync(search, SearchText.ToLower(), filter, order, instrument, SearchAuthorText);

                NoResults = DataItems.Count < 1;
            }
            finally
            {
                IsLoading = false;
                IsPlaceholder = false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
