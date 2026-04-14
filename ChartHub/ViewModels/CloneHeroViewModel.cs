using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Avalonia.Threading;

using ChartHub.Localization;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Strings;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

namespace ChartHub.ViewModels;

public class CloneHeroViewModel : INotifyPropertyChanged, IDisposable
{
    public bool IsCompanionMode => OperatingSystem.IsAndroid();
    public bool IsDesktopMode => !OperatingSystem.IsAndroid();

    private readonly AppGlobalSettings? _globalSettings;
    private readonly IChartHubServerApiClient? _serverApiClient;
    private readonly Func<Action, Task> _uiInvoke;
    private List<ChartHubServerCloneHeroSongResponse> _serverSongsCache = [];
    private string? _lastDeletedSongId;
    private bool _isArtistDrilldownActive;

    private bool _hasInitialized;
    public bool HasInitialized
    {
        get => _hasInitialized;
        private set
        {
            if (_hasInitialized == value)
            {
                return;
            }

            _hasInitialized = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowStartupBlockingState));
            OnPropertyChanged(nameof(ShowPostStartupStatus));
        }
    }

    private bool _isStartupScanInProgress;
    public bool IsStartupScanInProgress
    {
        get => _isStartupScanInProgress;
        private set
        {
            if (_isStartupScanInProgress == value)
            {
                return;
            }

            _isStartupScanInProgress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowStartupBlockingState));
            OnPropertyChanged(nameof(ShowPostStartupStatus));
        }
    }

    public bool ShowStartupBlockingState => !HasInitialized || IsStartupScanInProgress;
    public bool ShowPostStartupStatus => HasReconciliationStatus && !ShowStartupBlockingState;

    private ObservableCollection<string> _artists = [];
    public ObservableCollection<string> Artists
    {
        get => _artists;
        private set
        {
            _artists = value;
            OnPropertyChanged();
        }
    }

    private string? _selectedArtist;
    public string? SelectedArtist
    {
        get => _selectedArtist;
        set
        {
            if (_selectedArtist == value)
            {
                return;
            }

            _selectedArtist = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MobileHeaderTitle));
            ObserveBackgroundTask(LoadSongsForSelectedArtistAsync(), "Clone Hero artist selection changed");
        }
    }

    public bool IsArtistDrilldownActive
    {
        get => _isArtistDrilldownActive;
        private set
        {
            if (_isArtistDrilldownActive == value)
            {
                return;
            }

            _isArtistDrilldownActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowMobileArtistsList));
            OnPropertyChanged(nameof(ShowMobileSongsList));
            OnPropertyChanged(nameof(MobileHeaderTitle));
        }
    }

    public bool ShowMobileArtistsList => IsCompanionMode && !IsArtistDrilldownActive;
    public bool ShowMobileSongsList => IsCompanionMode && IsArtistDrilldownActive;
    public string MobileHeaderTitle => IsArtistDrilldownActive
        ? (string.IsNullOrWhiteSpace(SelectedArtist) ? UiLocalization.Get("CloneHero.MobileSongsHeader") : SelectedArtist)
        : UiLocalization.Get("CloneHero.MobileArtistsHeader");

    private ObservableCollection<CloneHeroLibrarySongItem> _songs = [];
    public ObservableCollection<CloneHeroLibrarySongItem> Songs
    {
        get => _songs;
        private set
        {
            _songs = value;
            OnPropertyChanged();
        }
    }

    private CloneHeroLibrarySongItem? _selectedSong;
    public CloneHeroLibrarySongItem? SelectedSong
    {
        get => _selectedSong;
        set
        {
            _selectedSong = value;
            OnPropertyChanged();
            _deleteSongCommand.NotifyCanExecuteChanged();
            _restoreLastDeletedSongCommand.NotifyCanExecuteChanged();
        }
    }

    public CloneHeroPageStrings PageStrings { get; }

    private bool _isReconciling;
    public bool IsReconciling
    {
        get => _isReconciling;
        private set
        {
            if (_isReconciling == value)
            {
                return;
            }

            _isReconciling = value;
            OnPropertyChanged();
            _deleteSongCommand.NotifyCanExecuteChanged();
            _restoreLastDeletedSongCommand.NotifyCanExecuteChanged();
        }
    }

    private string _reconciliationStatusMessage = string.Empty;
    public string ReconciliationStatusMessage
    {
        get => _reconciliationStatusMessage;
        private set
        {
            if (_reconciliationStatusMessage == value)
            {
                return;
            }

            _reconciliationStatusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasReconciliationStatus));
            OnPropertyChanged(nameof(ShowPostStartupStatus));
        }
    }

    public bool HasReconciliationStatus => !string.IsNullOrWhiteSpace(ReconciliationStatusMessage);

    private readonly AsyncRelayCommand _refreshLibraryCommand;
    public IAsyncRelayCommand RefreshLibraryCommand => _refreshLibraryCommand;

    private readonly RelayCommand _showArtistListCommand;
    public IRelayCommand ShowArtistListCommand => _showArtistListCommand;

    private readonly AsyncRelayCommand _deleteSongCommand;
    public IAsyncRelayCommand DeleteSongCommand => _deleteSongCommand;

    private readonly AsyncRelayCommand _restoreLastDeletedSongCommand;
    public IAsyncRelayCommand RestoreLastDeletedSongCommand => _restoreLastDeletedSongCommand;

    public CloneHeroViewModel(
        AppGlobalSettings? globalSettings = null,
        IChartHubServerApiClient? serverApiClient = null,
        Func<Action, Task>? uiInvoke = null)
    {
        _globalSettings = globalSettings;
        _serverApiClient = serverApiClient;
        _uiInvoke = uiInvoke ?? (async action => await Dispatcher.UIThread.InvokeAsync(action));

        PageStrings = new CloneHeroPageStrings();

        _refreshLibraryCommand = new AsyncRelayCommand(() => RefreshArtistsAsync(CancellationToken.None));
        _showArtistListCommand = new RelayCommand(ShowArtistList);
        _deleteSongCommand = new AsyncRelayCommand(DeleteSelectedSongAsync, () => SelectedSong is not null && !IsReconciling && HasServerConnection());
        _restoreLastDeletedSongCommand = new AsyncRelayCommand(RestoreLastDeletedSongAsync, CanRestoreLastDeletedSong);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (HasInitialized)
        {
            return;
        }

        IsStartupScanInProgress = true;
        try
        {
            await RefreshArtistsAsync(cancellationToken).ConfigureAwait(false);
            HasInitialized = true;
        }
        finally
        {
            IsStartupScanInProgress = false;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await RefreshArtistsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshArtistsAsync(CancellationToken cancellationToken)
    {
        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            _serverSongsCache = [];
            await _uiInvoke(() =>
            {
                Artists = [];
                Songs = [];
                SelectedArtist = null;
                IsArtistDrilldownActive = false;
                ReconciliationStatusMessage = UiLocalization.Get("CloneHero.ConfigureLoad");
            });
            return;
        }

        IReadOnlyList<ChartHubServerCloneHeroSongResponse> songs = await _serverApiClient!
            .ListCloneHeroSongsAsync(baseUrl, bearerToken, cancellationToken)
            .ConfigureAwait(false);

        _serverSongsCache = songs
            .OrderBy(song => song.Artist, StringComparer.OrdinalIgnoreCase)
            .ThenBy(song => song.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        IReadOnlyList<string> serverArtists = _serverSongsCache
            .Select(song => string.IsNullOrWhiteSpace(song.Artist) ? UiLocalization.Get("CloneHero.UnknownArtist") : song.Artist)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(artist => artist, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await _uiInvoke(() =>
        {
            Artists = new ObservableCollection<string>(serverArtists);

            if (Artists.Count == 0)
            {
                Songs = [];
                SelectedArtist = null;
                IsArtistDrilldownActive = false;
                ReconciliationStatusMessage = string.Empty;
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedArtist) || !Artists.Contains(SelectedArtist))
            {
                if (IsCompanionMode)
                {
                    SelectedArtist = null;
                    Songs = [];
                    SelectedSong = null;
                    IsArtistDrilldownActive = false;
                }
                else
                {
                    SelectedArtist = Artists[0];
                }
            }
            else
            {
                ApplySongsForSelectedArtist();
            }

            ReconciliationStatusMessage = string.Empty;
        });
    }

    private async Task LoadSongsForSelectedArtistAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        await _uiInvoke(ApplySongsForSelectedArtist);
    }

    private void ApplySongsForSelectedArtist()
    {
        if (string.IsNullOrWhiteSpace(SelectedArtist))
        {
            Songs = [];
            IsArtistDrilldownActive = false;
            return;
        }

        Songs = new ObservableCollection<CloneHeroLibrarySongItem>(_serverSongsCache
            .Where(song => string.Equals(song.Artist, SelectedArtist, StringComparison.OrdinalIgnoreCase))
            .Select(song => new CloneHeroLibrarySongItem
            {
                SongId = song.SongId,
                Artist = string.IsNullOrWhiteSpace(song.Artist) ? UiLocalization.Get("CloneHero.UnknownArtist") : song.Artist,
                Title = string.IsNullOrWhiteSpace(song.Title) ? UiLocalization.Get("CloneHero.UnknownSong") : song.Title,
                Charter = string.IsNullOrWhiteSpace(song.Charter) ? UiLocalization.Get("CloneHero.UnknownCharter") : song.Charter,
                Source = song.Source,
                SourceId = song.SourceId,
                LocalPath = song.InstalledPath ?? string.Empty,
                InstallRelativePath = song.InstalledRelativePath ?? string.Empty,
            }));

        SelectedSong = Songs.Count > 0 ? Songs[0] : null;
        if (IsCompanionMode)
        {
            IsArtistDrilldownActive = true;
        }
    }

    private void ShowArtistList()
    {
        if (!IsCompanionMode)
        {
            return;
        }

        SelectedArtist = null;
        Songs = [];
        SelectedSong = null;
        IsArtistDrilldownActive = false;
    }

    private async Task DeleteSelectedSongAsync()
    {
        if (SelectedSong is null)
        {
            return;
        }

        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            ReconciliationStatusMessage = UiLocalization.Get("CloneHero.ConfigureDelete");
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedSong.SongId))
        {
            ReconciliationStatusMessage = UiLocalization.Get("CloneHero.MissingServerId");
            return;
        }

        string deletedSongId = SelectedSong.SongId;

        IsReconciling = true;
        try
        {
            await _serverApiClient!
                .RequestDeleteCloneHeroSongAsync(baseUrl, bearerToken, deletedSongId, CancellationToken.None)
                .ConfigureAwait(false);

            await _uiInvoke(() =>
            {
                _lastDeletedSongId = deletedSongId;
                _restoreLastDeletedSongCommand.NotifyCanExecuteChanged();
            });
            await RefreshArtistsAsync(CancellationToken.None).ConfigureAwait(false);
            await _uiInvoke(() => ReconciliationStatusMessage = UiLocalization.Get("CloneHero.Deleted"));
        }
        catch (Exception ex)
        {
            await _uiInvoke(() => ReconciliationStatusMessage = UiLocalization.Format("CloneHero.DeleteFailed", ex.Message));
            Logger.LogError("CloneHero", "Delete selected song from server failed", ex, new Dictionary<string, object?>
            {
                ["songId"] = deletedSongId,
            });
        }
        finally
        {
            await _uiInvoke(() => IsReconciling = false);
        }
    }

    private bool CanRestoreLastDeletedSong()
    {
        return !string.IsNullOrWhiteSpace(_lastDeletedSongId) && !IsReconciling && HasServerConnection();
    }

    private async Task RestoreLastDeletedSongAsync()
    {
        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            ReconciliationStatusMessage = UiLocalization.Get("CloneHero.ConfigureRestore");
            return;
        }

        if (string.IsNullOrWhiteSpace(_lastDeletedSongId))
        {
            ReconciliationStatusMessage = UiLocalization.Get("CloneHero.NoRestoreTarget");
            return;
        }

        string deletedSongId = _lastDeletedSongId;

        IsReconciling = true;
        try
        {
            await _serverApiClient!
                .RequestRestoreCloneHeroSongAsync(baseUrl, bearerToken, deletedSongId, CancellationToken.None)
                .ConfigureAwait(false);

            await _uiInvoke(() =>
            {
                _lastDeletedSongId = null;
                _restoreLastDeletedSongCommand.NotifyCanExecuteChanged();
            });
            await RefreshArtistsAsync(CancellationToken.None).ConfigureAwait(false);
            await _uiInvoke(() => ReconciliationStatusMessage = UiLocalization.Get("CloneHero.Restored"));
        }
        catch (Exception ex)
        {
            await _uiInvoke(() => ReconciliationStatusMessage = UiLocalization.Format("CloneHero.RestoreFailed", ex.Message));
            Logger.LogError("CloneHero", "Restore deleted song failed", ex, new Dictionary<string, object?>
            {
                ["songId"] = deletedSongId,
            });
        }
        finally
        {
            await _uiInvoke(() => IsReconciling = false);
        }
    }

    private static void ObserveBackgroundTask(Task task, string context)
    {
        _ = task.ContinueWith(t =>
        {
            Exception? ex = t.Exception?.GetBaseException();
            if (ex is not null)
            {
                Logger.LogError("CloneHero", $"{context} failed", ex);
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        // no-op for now
    }

    private bool TryGetServerConnection(out string baseUrl, out string bearerToken)
    {
        if (_globalSettings is null || _serverApiClient is null)
        {
            baseUrl = string.Empty;
            bearerToken = string.Empty;
            return false;
        }

        baseUrl = NormalizeApiBaseUrl(_globalSettings.ServerApiBaseUrl);
        bearerToken = _globalSettings.ServerApiAuthToken?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(bearerToken);
    }

    private bool HasServerConnection()
    {
        return TryGetServerConnection(out _, out _);
    }

    private static string NormalizeApiBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return string.Empty;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }
}
