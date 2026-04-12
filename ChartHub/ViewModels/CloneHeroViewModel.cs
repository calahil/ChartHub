using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Strings;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

namespace ChartHub.ViewModels;

public class CloneHeroViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppGlobalSettings? _globalSettings;
    private readonly IChartHubServerApiClient? _serverApiClient;
    private readonly IDesktopPathOpener _desktopPathOpener;
    private List<ChartHubServerCloneHeroSongResponse> _serverSongsCache = [];
    private string? _lastDeletedSongId;

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
            ObserveBackgroundTask(LoadSongsForSelectedArtistAsync(), "Clone Hero artist selection changed");
        }
    }

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
            _openSongFolderCommand.NotifyCanExecuteChanged();
            _openSongIniCommand.NotifyCanExecuteChanged();
            _reParseMetadataCommand.NotifyCanExecuteChanged();
            _reconcileThisSongCommand.NotifyCanExecuteChanged();
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
            _reconcileLibraryCommand.NotifyCanExecuteChanged();
            _reParseMetadataCommand.NotifyCanExecuteChanged();
            _reconcileThisSongCommand.NotifyCanExecuteChanged();
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

    private readonly AsyncRelayCommand _openSongFolderCommand;
    public IAsyncRelayCommand OpenSongFolderCommand => _openSongFolderCommand;

    private readonly AsyncRelayCommand _openSongIniCommand;
    public IAsyncRelayCommand OpenSongIniCommand => _openSongIniCommand;

    private readonly AsyncRelayCommand _reconcileLibraryCommand;
    public IAsyncRelayCommand ReconcileLibraryCommand => _reconcileLibraryCommand;

    private readonly AsyncRelayCommand _reParseMetadataCommand;
    public IAsyncRelayCommand ReParseMetadataCommand => _reParseMetadataCommand;

    private readonly AsyncRelayCommand _reconcileThisSongCommand;
    public IAsyncRelayCommand ReconcileThisSongCommand => _reconcileThisSongCommand;

    private readonly AsyncRelayCommand _deleteSongCommand;
    public IAsyncRelayCommand DeleteSongCommand => _deleteSongCommand;

    private readonly AsyncRelayCommand _restoreLastDeletedSongCommand;
    public IAsyncRelayCommand RestoreLastDeletedSongCommand => _restoreLastDeletedSongCommand;

    public CloneHeroViewModel(
        LibraryCatalogService libraryCatalog,
        SongIngestionCatalogService ingestionCatalog,
        IDesktopPathOpener desktopPathOpener,
        ILocalFileDeletionService localFileDeletionService,
        ICloneHeroLibraryReconciliationService? reconciliationService = null,
        AppGlobalSettings? globalSettings = null,
        IChartHubServerApiClient? serverApiClient = null)
    {
        _ = libraryCatalog;
        _ = ingestionCatalog;
        _ = localFileDeletionService;
        _ = reconciliationService;

        _globalSettings = globalSettings;
        _serverApiClient = serverApiClient;
        _desktopPathOpener = desktopPathOpener;

        PageStrings = new CloneHeroPageStrings();

        _refreshLibraryCommand = new AsyncRelayCommand(() => RefreshArtistsAsync(CancellationToken.None));
        _openSongFolderCommand = new AsyncRelayCommand(OpenSelectedSongFolderAsync, () => SelectedSong is not null);
        _openSongIniCommand = new AsyncRelayCommand(OpenSelectedSongIniLocationAsync, () => SelectedSong is not null);
        _reconcileLibraryCommand = new AsyncRelayCommand(ReconcileLibraryAsync, () => false);
        _reParseMetadataCommand = new AsyncRelayCommand(ReParseSelectedSongMetadataAsync, () => false);
        _reconcileThisSongCommand = new AsyncRelayCommand(ReconcileSelectedSongAsync, () => false);
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

    private async Task ReconcileLibraryAsync()
    {
        await Task.CompletedTask;
    }

    private async Task RefreshArtistsAsync(CancellationToken cancellationToken)
    {
        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            _serverSongsCache = [];
            Artists = [];
            Songs = [];
            SelectedArtist = null;
            ReconciliationStatusMessage = "Configure ChartHub.Server URL and token to load Clone Hero library.";
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
            .Select(song => string.IsNullOrWhiteSpace(song.Artist) ? "Unknown Artist" : song.Artist)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(artist => artist, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Artists = new ObservableCollection<string>(serverArtists);

        if (Artists.Count == 0)
        {
            Songs = [];
            SelectedArtist = null;
            ReconciliationStatusMessage = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedArtist) || !Artists.Contains(SelectedArtist))
        {
            SelectedArtist = Artists[0];
        }
        else
        {
            await LoadSongsForSelectedArtistAsync(cancellationToken).ConfigureAwait(false);
        }

        ReconciliationStatusMessage = string.Empty;
    }

    private async Task LoadSongsForSelectedArtistAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(SelectedArtist))
        {
            Songs = [];
            return;
        }

        Songs = new ObservableCollection<CloneHeroLibrarySongItem>(_serverSongsCache
            .Where(song => string.Equals(song.Artist, SelectedArtist, StringComparison.OrdinalIgnoreCase))
            .Select(song => new CloneHeroLibrarySongItem
            {
                SongId = song.SongId,
                Artist = string.IsNullOrWhiteSpace(song.Artist) ? "Unknown Artist" : song.Artist,
                Title = string.IsNullOrWhiteSpace(song.Title) ? "Unknown Song" : song.Title,
                Charter = string.IsNullOrWhiteSpace(song.Charter) ? "Unknown Charter" : song.Charter,
                Source = song.Source,
                SourceId = song.SourceId,
                LocalPath = song.InstalledPath ?? string.Empty,
                InstallRelativePath = song.InstalledRelativePath ?? string.Empty,
            }));

        SelectedSong = Songs.Count > 0 ? Songs[0] : null;

        _openSongFolderCommand.NotifyCanExecuteChanged();
        _openSongIniCommand.NotifyCanExecuteChanged();

        await Task.CompletedTask;
    }

    private async Task ReParseSelectedSongMetadataAsync()
    {
        await Task.CompletedTask;
    }

    private async Task ReconcileSelectedSongAsync()
    {
        await Task.CompletedTask;
    }

    private async Task DeleteSelectedSongAsync()
    {
        if (SelectedSong is null)
        {
            return;
        }

        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            ReconciliationStatusMessage = "Configure ChartHub.Server URL and token to delete Clone Hero songs.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedSong.SongId))
        {
            ReconciliationStatusMessage = "Selected song is missing server ID and cannot be deleted.";
            return;
        }

        string deletedSongId = SelectedSong.SongId;

        IsReconciling = true;
        try
        {
            await _serverApiClient!
                .RequestDeleteCloneHeroSongAsync(baseUrl, bearerToken, deletedSongId, CancellationToken.None)
                .ConfigureAwait(false);

            _lastDeletedSongId = deletedSongId;
            _restoreLastDeletedSongCommand.NotifyCanExecuteChanged();
            await RefreshArtistsAsync(CancellationToken.None).ConfigureAwait(false);
            ReconciliationStatusMessage = "Selected song deleted from server library.";
        }
        catch (Exception ex)
        {
            ReconciliationStatusMessage = $"Failed to delete song: {ex.Message}";
            Logger.LogError("CloneHero", "Delete selected song from server failed", ex, new Dictionary<string, object?>
            {
                ["songId"] = deletedSongId,
            });
        }
        finally
        {
            IsReconciling = false;
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
            ReconciliationStatusMessage = "Configure ChartHub.Server URL and token to restore Clone Hero songs.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_lastDeletedSongId))
        {
            ReconciliationStatusMessage = "No recently deleted song is available to restore.";
            return;
        }

        string deletedSongId = _lastDeletedSongId;

        IsReconciling = true;
        try
        {
            await _serverApiClient!
                .RequestRestoreCloneHeroSongAsync(baseUrl, bearerToken, deletedSongId, CancellationToken.None)
                .ConfigureAwait(false);

            _lastDeletedSongId = null;
            _restoreLastDeletedSongCommand.NotifyCanExecuteChanged();
            await RefreshArtistsAsync(CancellationToken.None).ConfigureAwait(false);
            ReconciliationStatusMessage = "Last deleted song restored.";
        }
        catch (Exception ex)
        {
            ReconciliationStatusMessage = $"Failed to restore song: {ex.Message}";
            Logger.LogError("CloneHero", "Restore deleted song failed", ex, new Dictionary<string, object?>
            {
                ["songId"] = deletedSongId,
            });
        }
        finally
        {
            IsReconciling = false;
        }
    }

    private async Task OpenSelectedSongFolderAsync()
    {
        if (SelectedSong is null)
        {
            return;
        }

        await _desktopPathOpener.OpenDirectoryAsync(SelectedSong.LocalPath).ConfigureAwait(false);
    }

    private async Task OpenSelectedSongIniLocationAsync()
    {
        if (SelectedSong is null)
        {
            return;
        }

        string? targetDir = Directory.Exists(SelectedSong.LocalPath)
            ? SelectedSong.LocalPath
            : Path.GetDirectoryName(SelectedSong.SongIniPath);

        if (!string.IsNullOrWhiteSpace(targetDir) && Directory.Exists(targetDir))
        {
            await _desktopPathOpener.OpenDirectoryAsync(targetDir).ConfigureAwait(false);
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
