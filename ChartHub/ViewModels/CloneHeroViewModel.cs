using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Strings;
using ChartHub.Utilities;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChartHub.ViewModels
{
    public class CloneHeroViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly LibraryCatalogService _libraryCatalog;
        private readonly IDesktopPathOpener _desktopPathOpener;

        private bool _hasInitialized;
        public bool HasInitialized
        {
            get => _hasInitialized;
            private set
            {
                if (_hasInitialized == value)
                    return;

                _hasInitialized = value;
                OnPropertyChanged();
            }
        }

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
                    return;

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
            }
        }

        public CloneHeroPageStrings PageStrings { get; }

        private readonly AsyncRelayCommand _refreshLibraryCommand;
        public IAsyncRelayCommand RefreshLibraryCommand => _refreshLibraryCommand;

        private readonly AsyncRelayCommand _openSongFolderCommand;
        public IAsyncRelayCommand OpenSongFolderCommand => _openSongFolderCommand;

        private readonly AsyncRelayCommand _openSongIniCommand;
        public IAsyncRelayCommand OpenSongIniCommand => _openSongIniCommand;

        public CloneHeroViewModel(
            LibraryCatalogService libraryCatalog,
            IDesktopPathOpener desktopPathOpener)
        {
            _libraryCatalog = libraryCatalog;
            _desktopPathOpener = desktopPathOpener;
            PageStrings = new CloneHeroPageStrings();

            _refreshLibraryCommand = new AsyncRelayCommand(() => RefreshArtistsAsync(CancellationToken.None));
            _openSongFolderCommand = new AsyncRelayCommand(OpenSelectedSongFolderAsync, () => SelectedSong is not null);
            _openSongIniCommand = new AsyncRelayCommand(OpenSelectedSongIniLocationAsync, () => SelectedSong is not null);
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (HasInitialized)
                return;

            await RefreshArtistsAsync(cancellationToken);
            HasInitialized = true;
        }

        /// <summary>
        /// Refreshes the Clone Hero library from the database. Called after installs complete to update the UI.
        /// </summary>
        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            await RefreshArtistsAsync(cancellationToken);
        }

        private async Task RefreshArtistsAsync(CancellationToken cancellationToken)
        {
            var artists = await _libraryCatalog.GetArtistsAsync(cancellationToken);
            Artists = new ObservableCollection<string>(artists);

            if (Artists.Count == 0)
            {
                Songs = [];
                SelectedArtist = null;
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedArtist) || !Artists.Contains(SelectedArtist))
                SelectedArtist = Artists[0];
            else
                await LoadSongsForSelectedArtistAsync(cancellationToken);
        }

        private async Task LoadSongsForSelectedArtistAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(SelectedArtist))
            {
                Songs = [];
                return;
            }

            var entries = await _libraryCatalog.GetEntriesByArtistAsync(SelectedArtist, cancellationToken);
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
                SelectedSong = Songs[0];
            else
                SelectedSong = null;

            _openSongFolderCommand.NotifyCanExecuteChanged();
            _openSongIniCommand.NotifyCanExecuteChanged();
        }

        private async Task OpenSelectedSongFolderAsync()
        {
            if (SelectedSong is null)
                return;

            await _desktopPathOpener.OpenDirectoryAsync(SelectedSong.LocalPath);
        }

        private async Task OpenSelectedSongIniLocationAsync()
        {
            if (SelectedSong is null)
                return;

            var targetDir = Directory.Exists(SelectedSong.LocalPath)
                ? SelectedSong.LocalPath
                : Path.GetDirectoryName(SelectedSong.SongIniPath);

            if (!string.IsNullOrWhiteSpace(targetDir) && Directory.Exists(targetDir))
                await _desktopPathOpener.OpenDirectoryAsync(targetDir);
        }

        private static void ObserveBackgroundTask(Task task, string context)
        {
            _ = task.ContinueWith(t =>
            {
                var ex = t.Exception?.GetBaseException();
                if (ex is not null)
                    Logger.LogError("CloneHero", $"{context} failed", ex);
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
}
