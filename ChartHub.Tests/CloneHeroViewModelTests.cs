using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class CloneHeroViewModelTests
{
    [Fact]
    public void InitialState_ShowsStartupBlockingState()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-vm-initial");
        CloneHeroViewModel sut = CreateViewModel(temp.RootPath, CreateSettings(temp.RootPath, string.Empty, string.Empty), new FakeServerApiClient());

        Assert.False(sut.HasInitialized);
        Assert.True(sut.ShowStartupBlockingState);
        Assert.False(sut.IsStartupScanInProgress);
    }

    [Fact]
    public async Task InitializeAsync_WithoutServerConnection_ShowsConfigurationMessage()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-vm-no-server");
        CloneHeroViewModel sut = CreateViewModel(temp.RootPath, CreateSettings(temp.RootPath, string.Empty, string.Empty), new FakeServerApiClient());

        await sut.InitializeAsync();

        Assert.True(sut.HasInitialized);
        Assert.Empty(sut.Artists);
        Assert.Empty(sut.Songs);
        Assert.Contains("Configure ChartHub.Server URL and token", sut.ReconciliationStatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeAsync_LoadsArtistsAndFiltersSongsFromServer()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-vm-server-filter");
        var fakeServer = new FakeServerApiClient();
        fakeServer.Songs =
        [
            new ChartHubServerCloneHeroSongResponse(
                SongId: "song-a-1",
                Source: "rhythmverse",
                SourceId: "rv-a1",
                Artist: "Artist A",
                Title: "Song One",
                Charter: "Charter A",
                SourceMd5: null,
                SourceChartHash: null,
                SourceUrl: "https://example.test/a1",
                InstalledPath: "/songs/Artist A/Song One/Charter A__rhythmverse",
                InstalledRelativePath: "Artist A/Song One/Charter A__rhythmverse",
                UpdatedAtUtc: DateTimeOffset.UtcNow),
            new ChartHubServerCloneHeroSongResponse(
                SongId: "song-b-1",
                Source: "encore",
                SourceId: "encore|chartId=5|md5=abcd",
                Artist: "Artist B",
                Title: "Song Two",
                Charter: "Charter B",
                SourceMd5: "abcd",
                SourceChartHash: null,
                SourceUrl: "https://example.test/b1",
                InstalledPath: "/songs/Artist B/Song Two/Charter B__encore",
                InstalledRelativePath: "Artist B/Song Two/Charter B__encore",
                UpdatedAtUtc: DateTimeOffset.UtcNow),
        ];

        CloneHeroViewModel sut = CreateViewModel(
            temp.RootPath,
            CreateSettings(temp.RootPath, "http://localhost:5000", "token"),
            fakeServer);

        await sut.InitializeAsync();

        Assert.Equal(2, sut.Artists.Count);
        Assert.Equal("Artist A", sut.SelectedArtist);
        Assert.Single(sut.Songs);
        Assert.Equal("Song One", sut.Songs[0].Title);

        sut.SelectedArtist = "Artist B";
        Assert.True(SpinWait.SpinUntil(() => sut.Songs.Count == 1 && sut.Songs[0].Title == "Song Two", TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task DeleteCommand_IsEnabledInServerMode_WhenSongSelected()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-vm-delete-enabled");
        var fakeServer = new FakeServerApiClient
        {
            Songs =
            [
                new ChartHubServerCloneHeroSongResponse(
                    SongId: "song-a-1",
                    Source: "rhythmverse",
                    SourceId: "rv-a1",
                    Artist: "Artist A",
                    Title: "Song One",
                    Charter: "Charter A",
                    SourceMd5: null,
                    SourceChartHash: null,
                    SourceUrl: "https://example.test/a1",
                    InstalledPath: "/songs/Artist A/Song One/Charter A__rhythmverse",
                    InstalledRelativePath: "Artist A/Song One/Charter A__rhythmverse",
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
            ],
        };

        CloneHeroViewModel sut = CreateViewModel(
            temp.RootPath,
            CreateSettings(temp.RootPath, "http://localhost:5000", "token"),
            fakeServer);

        await sut.InitializeAsync();

        Assert.NotNull(sut.SelectedSong);
        Assert.True(sut.DeleteSongCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeleteSongCommand_CallsServerDeleteAndRefreshesSongs()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-vm-delete-calls-server");
        var fakeServer = new FakeServerApiClient
        {
            Songs =
            [
                new ChartHubServerCloneHeroSongResponse(
                    SongId: "song-a-1",
                    Source: "rhythmverse",
                    SourceId: "rv-a1",
                    Artist: "Artist A",
                    Title: "Song One",
                    Charter: "Charter A",
                    SourceMd5: null,
                    SourceChartHash: null,
                    SourceUrl: "https://example.test/a1",
                    InstalledPath: "/songs/Artist A/Song One/Charter A__rhythmverse",
                    InstalledRelativePath: "Artist A/Song One/Charter A__rhythmverse",
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
            ],
        };

        CloneHeroViewModel sut = CreateViewModel(
            temp.RootPath,
            CreateSettings(temp.RootPath, "http://localhost:5000", "token"),
            fakeServer);

        await sut.InitializeAsync();
        Assert.NotNull(sut.SelectedSong);

        await sut.DeleteSongCommand.ExecuteAsync(null);

        Assert.Single(fakeServer.DeletedSongIds);
        Assert.Equal("song-a-1", fakeServer.DeletedSongIds[0]);
        Assert.Empty(sut.Songs);
        Assert.Contains("deleted from server library", sut.ReconciliationStatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreLastDeletedSongCommand_RestoresSongInServerLibrary()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-vm-restore-last");
        var fakeServer = new FakeServerApiClient
        {
            Songs =
            [
                new ChartHubServerCloneHeroSongResponse(
                    SongId: "song-a-1",
                    Source: "rhythmverse",
                    SourceId: "rv-a1",
                    Artist: "Artist A",
                    Title: "Song One",
                    Charter: "Charter A",
                    SourceMd5: null,
                    SourceChartHash: null,
                    SourceUrl: "https://example.test/a1",
                    InstalledPath: "/songs/Artist A/Song One/Charter A__rhythmverse",
                    InstalledRelativePath: "Artist A/Song One/Charter A__rhythmverse",
                    UpdatedAtUtc: DateTimeOffset.UtcNow),
            ],
        };

        CloneHeroViewModel sut = CreateViewModel(
            temp.RootPath,
            CreateSettings(temp.RootPath, "http://localhost:5000", "token"),
            fakeServer);

        await sut.InitializeAsync();
        await sut.DeleteSongCommand.ExecuteAsync(null);

        Assert.Empty(sut.Songs);
        Assert.True(sut.RestoreLastDeletedSongCommand.CanExecute(null));

        await sut.RestoreLastDeletedSongCommand.ExecuteAsync(null);

        Assert.Single(fakeServer.RestoredSongIds);
        Assert.Equal("song-a-1", fakeServer.RestoredSongIds[0]);
        Assert.Single(sut.Songs);
        Assert.Contains("restored", sut.ReconciliationStatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static CloneHeroViewModel CreateViewModel(
        string rootPath,
        AppGlobalSettings settings,
        IChartHubServerApiClient serverApiClient)
    {
        _ = rootPath;
        return new CloneHeroViewModel(
            settings,
            serverApiClient,
            action => { action(); return Task.CompletedTask; });
    }

    private static AppGlobalSettings CreateSettings(string rootPath, string baseUrl, string token)
    {
        _ = rootPath;

        AppConfigRoot config = new()
        {
            Runtime = new RuntimeAppConfig
            {
                ServerApiBaseUrl = baseUrl,
                ServerApiAuthToken = token,
            },
        };

        return new AppGlobalSettings(new FakeSettingsOrchestrator(config));
    }

    private sealed class FakeServerApiClient : IChartHubServerApiClient
    {
        public IReadOnlyList<ChartHubServerCloneHeroSongResponse> Songs { get; set; } = [];
        public List<string> DeletedSongIds { get; } = [];
        public List<string> RestoredSongIds { get; } = [];
        private readonly Dictionary<string, ChartHubServerCloneHeroSongResponse> _deletedSongs = new(StringComparer.Ordinal);

        public Task<ChartHubServerAuthExchangeResponse> ExchangeGoogleTokenAsync(string baseUrl, string googleIdToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ChartHubServerDownloadJobResponse> CreateDownloadJobAsync(string baseUrl, string bearerToken, ChartHubServerCreateDownloadJobRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ChartHubServerDownloadJobResponse>> ListDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ChartHubServerDownloadJobResponse>>([]);

        public IAsyncEnumerable<IReadOnlyList<ChartHubServerDownloadProgressEvent>> StreamDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<IReadOnlyList<ChartHubServerDownloadProgressEvent>>();

        public Task<ChartHubServerDownloadJobResponse> RequestInstallDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RequestCancelDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ChartHubServerCloneHeroSongResponse>> ListCloneHeroSongsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => Task.FromResult(Songs);

        public Task<ChartHubServerCloneHeroSongResponse> GetCloneHeroSongAsync(string baseUrl, string bearerToken, string songId, CancellationToken cancellationToken = default)
        {
            ChartHubServerCloneHeroSongResponse? song = Songs.FirstOrDefault(item => string.Equals(item.SongId, songId, StringComparison.Ordinal));
            if (song is null)
            {
                throw new InvalidOperationException("Song not found.");
            }

            return Task.FromResult(song);
        }

        public Task RequestDeleteCloneHeroSongAsync(string baseUrl, string bearerToken, string songId, CancellationToken cancellationToken = default)
        {
            DeletedSongIds.Add(songId);
            ChartHubServerCloneHeroSongResponse? deletedSong = Songs.FirstOrDefault(song => string.Equals(song.SongId, songId, StringComparison.Ordinal));
            if (deletedSong is not null)
            {
                _deletedSongs[songId] = deletedSong;
            }

            Songs = Songs.Where(song => !string.Equals(song.SongId, songId, StringComparison.Ordinal)).ToArray();
            return Task.CompletedTask;
        }

        public Task<ChartHubServerCloneHeroSongResponse> RequestRestoreCloneHeroSongAsync(string baseUrl, string bearerToken, string songId, CancellationToken cancellationToken = default)
        {
            if (!_deletedSongs.TryGetValue(songId, out ChartHubServerCloneHeroSongResponse? deletedSong))
            {
                throw new InvalidOperationException("Song was not deleted.");
            }

            RestoredSongIds.Add(songId);
            Songs = [.. Songs, deletedSong];
            _deletedSongs.Remove(songId);
            return Task.FromResult(deletedSong);
        }
    }

    private sealed class FakeSettingsOrchestrator(AppConfigRoot current) : ISettingsOrchestrator
    {
        public AppConfigRoot Current { get; private set; } = current;

        public event Action<AppConfigRoot>? SettingsChanged;

        public Task<ConfigValidationResult> UpdateAsync(Action<AppConfigRoot> update, CancellationToken cancellationToken = default)
        {
            update(Current);
            SettingsChanged?.Invoke(Current);
            return Task.FromResult(ConfigValidationResult.Success);
        }

        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            SettingsChanged?.Invoke(Current);
            return Task.CompletedTask;
        }
    }
}
