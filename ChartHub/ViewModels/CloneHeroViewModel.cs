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
    private readonly LibraryCatalogService _libraryCatalog;
    private readonly SongIngestionCatalogService _ingestionCatalog;
    private readonly IDesktopPathOpener _desktopPathOpener;
    private readonly ILocalFileDeletionService _localFileDeletionService;
    private readonly ICloneHeroLibraryReconciliationService? _reconciliationService;

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
            _reParseMetadataCommand?.NotifyCanExecuteChanged();
            _reconcileThisSongCommand?.NotifyCanExecuteChanged();
            _deleteSongCommand?.NotifyCanExecuteChanged();
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
            _reParseMetadataCommand?.NotifyCanExecuteChanged();
            _reconcileThisSongCommand?.NotifyCanExecuteChanged();
            _deleteSongCommand?.NotifyCanExecuteChanged();
        }
    }

    private string _reconciliationStatusMessage = "";
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

    public CloneHeroViewModel(
        LibraryCatalogService libraryCatalog,
        SongIngestionCatalogService ingestionCatalog,
        IDesktopPathOpener desktopPathOpener,
        ILocalFileDeletionService localFileDeletionService,
        ICloneHeroLibraryReconciliationService? reconciliationService = null)
    {
        _libraryCatalog = libraryCatalog;
        _ingestionCatalog = ingestionCatalog;
        _desktopPathOpener = desktopPathOpener;
        _localFileDeletionService = localFileDeletionService;
        _reconciliationService = reconciliationService;
        PageStrings = new CloneHeroPageStrings();

        _refreshLibraryCommand = new AsyncRelayCommand(() => RefreshArtistsAsync(CancellationToken.None));
        _openSongFolderCommand = new AsyncRelayCommand(OpenSelectedSongFolderAsync, () => SelectedSong is not null);
        _openSongIniCommand = new AsyncRelayCommand(OpenSelectedSongIniLocationAsync, () => SelectedSong is not null);
        _reconcileLibraryCommand = new AsyncRelayCommand(ReconcileLibraryAsync, () => !IsReconciling);
        _reParseMetadataCommand = new AsyncRelayCommand(ReParseSelectedSongMetadataAsync, () => SelectedSong is not null && !IsReconciling);
        _reconcileThisSongCommand = new AsyncRelayCommand(ReconcileSelectedSongAsync, () => SelectedSong is not null && !IsReconciling);
        _deleteSongCommand = new AsyncRelayCommand(DeleteSelectedSongAsync, () => SelectedSong is not null && !IsReconciling);
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
            await RunReconciliationAsync(isStartup: true, cancellationToken);
            await RefreshArtistsAsync(cancellationToken);
            HasInitialized = true;
        }
        finally
        {
            IsStartupScanInProgress = false;
        }
    }

    /// <summary>
    /// Refreshes the Clone Hero library from the database. Called after installs complete to update the UI.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await RefreshArtistsAsync(cancellationToken);
    }

    private async Task ReconcileLibraryAsync()
    {
        await RunReconciliationAsync(isStartup: false, CancellationToken.None);
        await RefreshArtistsAsync(CancellationToken.None);
    }

    private async Task RunReconciliationAsync(bool isStartup, CancellationToken cancellationToken)
    {
        if (_reconciliationService is null || IsReconciling)
        {
            return;
        }

        IsReconciling = true;
        ReconciliationStatusMessage = isStartup
            ? "Reconciling Clone Hero library..."
            : "Running library reconciliation...";

        try
        {
            var progress = new Progress<CloneHeroReconciliationProgress>(update =>
            {
                if (update.TotalSongs > 0)
                {
                    ReconciliationStatusMessage = $"{update.Message} ({update.ProcessedSongs}/{update.TotalSongs})";
                }
                else
                {
                    ReconciliationStatusMessage = update.Message;
                }
            });

            CloneHeroReconciliationResult result = await _reconciliationService.ReconcileAsync(progress, cancellationToken);
            ReconciliationStatusMessage = $"Reconciliation complete. Scanned {result.Scanned}, updated {result.Updated}, renamed {result.Renamed}, failed {result.Failed}.";
        }
        catch (Exception ex)
        {
            ReconciliationStatusMessage = $"Reconciliation failed: {ex.Message}";
            Logger.LogError("CloneHero", "Library reconciliation failed", ex);
        }
        finally
        {
            IsReconciling = false;
        }
    }

    private async Task RefreshArtistsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> artists = await _libraryCatalog.GetArtistsAsync(cancellationToken);
        Artists = new ObservableCollection<string>(artists);

        if (Artists.Count == 0)
        {
            Songs = [];
            SelectedArtist = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedArtist) || !Artists.Contains(SelectedArtist))
        {
            SelectedArtist = Artists[0];
        }
        else
        {
            await LoadSongsForSelectedArtistAsync(cancellationToken);
        }
    }

    private async Task LoadSongsForSelectedArtistAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(SelectedArtist))
        {
            Songs = [];
            return;
        }

        IReadOnlyList<LibraryCatalogEntry> entries = await _libraryCatalog.GetEntriesByArtistAsync(SelectedArtist, cancellationToken);
        Songs = new ObservableCollection<CloneHeroLibrarySongItem>(entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.LocalPath))
            .Select(entry => new CloneHeroLibrarySongItem
            {
                Artist = string.IsNullOrWhiteSpace(entry.Artist) ? "Unknown Artist" : entry.Artist,
                Title = string.IsNullOrWhiteSpace(entry.Title) ? "Unknown Song" : entry.Title,
                Charter = string.IsNullOrWhiteSpace(entry.Charter) ? "Unknown Charter" : entry.Charter,
                Source = entry.Source,
                SourceId = entry.SourceId,
                LocalPath = entry.LocalPath!,
            }));

        if (Songs.Count > 0)
        {
            SelectedSong = Songs[0];
        }
        else
        {
            SelectedSong = null;
        }

        _openSongFolderCommand.NotifyCanExecuteChanged();
        _openSongIniCommand.NotifyCanExecuteChanged();
    }

    private async Task ReParseSelectedSongMetadataAsync()
    {
        if (SelectedSong is null || _reconciliationService is null)
        {
            return;
        }

        IsReconciling = true;
        ReconciliationStatusMessage = "Re-parsing metadata...";
        try
        {
            bool updated = await _reconciliationService.ReParseMetadataAsync(SelectedSong.LocalPath, CancellationToken.None);
            ReconciliationStatusMessage = updated
                ? "Metadata updated from song.ini."
                : "No changes — song.ini not found or entry unchanged.";
            await RefreshArtistsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ReconciliationStatusMessage = $"Failed to re-parse metadata: {ex.Message}";
            Logger.LogError("CloneHero", "Re-parse metadata failed", ex);
        }
        finally
        {
            IsReconciling = false;
        }
    }

    private async Task ReconcileSelectedSongAsync()
    {
        if (SelectedSong is null || _reconciliationService is null)
        {
            return;
        }

        IsReconciling = true;
        ReconciliationStatusMessage = "Reconciling song...";
        try
        {
            bool updated = await _reconciliationService.ReconcileSongDirectoryAsync(SelectedSong.LocalPath, CancellationToken.None);
            ReconciliationStatusMessage = updated
                ? "Song reconciled and catalog updated."
                : "No changes needed for this song.";
            await RefreshArtistsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ReconciliationStatusMessage = $"Failed to reconcile song: {ex.Message}";
            Logger.LogError("CloneHero", "Reconcile this song failed", ex);
        }
        finally
        {
            IsReconciling = false;
        }
    }

    private async Task DeleteSelectedSongAsync()
    {
        if (SelectedSong is null)
        {
            return;
        }

        CloneHeroLibrarySongItem selected = SelectedSong;
        IsReconciling = true;
        ReconciliationStatusMessage = "Deleting selected song...";

        try
        {
            await _localFileDeletionService.DeletePathIfExistsAsync(selected.LocalPath, CancellationToken.None);
            await _libraryCatalog.RemoveAsync(selected.Source, selected.SourceId, CancellationToken.None);

            SongIngestionRecord? linkedIngestion = await _ingestionCatalog
                .GetLatestIngestionBySourceKeyAsync(selected.Source, selected.SourceId, CancellationToken.None);
            if (linkedIngestion is not null)
            {
                await _ingestionCatalog.RemoveIngestionAsync(linkedIngestion.Id, CancellationToken.None);
            }

            ReconciliationStatusMessage = "Selected song deleted from disk and catalog.";
            await RefreshArtistsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ReconciliationStatusMessage = $"Failed to delete song: {ex.Message}";
            Logger.LogError("CloneHero", "Delete selected song failed", ex, new Dictionary<string, object?>
            {
                ["source"] = selected.Source,
                ["sourceId"] = selected.SourceId,
                ["localPath"] = selected.LocalPath,
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

        await _desktopPathOpener.OpenDirectoryAsync(SelectedSong.LocalPath);
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
            await _desktopPathOpener.OpenDirectoryAsync(targetDir);
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
}
