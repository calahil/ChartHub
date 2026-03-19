using System.Collections.ObjectModel;
using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Services.Transfers;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class EncoreViewModelTests
{
    [Fact]
    public async Task RefreshAsync_WithAdvancedFields_UsesAdvancedEndpoint()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-advanced");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.enchor.us"),
        };

        var api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpTransferOrchestrator(), catalog, new NoOpSettingsOrchestrator(), new SharedDownloadQueue())
        {
            AdvancedName = "Song",
            AdvancedAlbum = "Album",
            MinYear = "2000",
            MaxYear = "2020",
            HasIssues = true,
            Modchart = false,
        };

        await sut.RefreshAsync();

        Assert.Contains(handler.RequestUris, uri => uri.Contains("/search/advanced", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAsync_WithoutAdvancedFields_UsesGeneralSearchEndpoint()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-general");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.enchor.us"),
        };

        var api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpTransferOrchestrator(), catalog, new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();

        Assert.Contains(handler.RequestUris, uri => uri.Contains("/search", StringComparison.Ordinal) && !uri.Contains("/search/advanced", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAsync_WhenLegacyMd5CatalogEntryExists_MarksSongAsInLibrary_AndUsesChartIdSourceId()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-legacy-md5-membership");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        const string md5 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        await catalog.UpsertAsync(new LibraryCatalogEntry(
            LibrarySourceNames.Encore,
            md5,
            "Legacy Song",
            "Legacy Artist",
            "Legacy Charter",
            "/tmp/legacy.sng",
            DateTimeOffset.UtcNow));

        var handler = new RecordingHttpHandler(BuildEncoreResponseJson(chartId: 777, md5: md5));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.enchor.us"),
        };

        var api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpTransferOrchestrator(), catalog, new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();

        var song = Assert.Single(sut.DataItems);
        Assert.True(song.IsInLibrary);
        Assert.Equal("777", song.SourceId);
    }

    [Fact]
    public async Task DownloadSongAsync_PersistsBothChartIdAndMd5CatalogKeys()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-dual-key-upsert");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        const string md5 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        var handler = new RecordingHttpHandler(BuildEncoreResponseJson(chartId: 888, md5: md5));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.enchor.us"),
        };

        var api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpTransferOrchestrator(), catalog, new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();
        var song = Assert.Single(sut.DataItems);

        await sut.DownloadSongAsync(song);

        Assert.True(await catalog.IsInLibraryAsync(LibrarySourceNames.Encore, "888"));
        Assert.True(await catalog.IsInLibraryAsync(LibrarySourceNames.Encore, md5));
    }

    [Fact]
    public async Task RefreshAsync_MapsEncoreDurationsFromMilliseconds()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-ms-duration");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var handler = new RecordingHttpHandler(BuildEncoreResponseJson(chartId: 999, md5: "cccccccccccccccccccccccccccccccc"));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.enchor.us"),
        };

        var api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpTransferOrchestrator(), catalog, new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();

        var song = Assert.Single(sut.DataItems);
        Assert.Equal(210000, song.SongLengthMs);
        Assert.Equal("3:30", song.FormattedTime);
    }

    [Fact]
    public async Task DownloadSongAsync_UsesUnifiedViewSongFallbacksForEncore()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-viewsong-fallbacks");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        const string md5 = "dddddddddddddddddddddddddddddddd";

        var handler = new RecordingHttpHandler(BuildEncoreResponseJson(
            chartId: 321,
            md5: md5,
            songOverrides: """
                                    "name": null,
                                    "artist": null,
                                    "album": null,
                                    "genre": null,
                                    "year": null,
                                    "charter": null,
                                    "applicationUsername": "encore-user",
                                    "albumArtMd5": null,
                                    "song_length": 210000,
            """));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.enchor.us"),
        };

        var api = CreateApiService(catalog, httpClient);
        var transfer = new CapturingTransferOrchestrator();
        var sut = new EncoreViewModel(api, transfer, catalog, new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();
        var song = Assert.Single(sut.DataItems);

        await sut.DownloadSongAsync(song);

        Assert.NotNull(transfer.LastSong);
        Assert.Equal("Unknown Song", transfer.LastSong!.Title);
        Assert.Equal("Unknown Artist", transfer.LastSong.Artist);
        Assert.Equal("Unknown Album", transfer.LastSong.Album);
        Assert.Equal("Unknown Genre", transfer.LastSong.Genre);
        Assert.Equal(string.Empty, transfer.LastSong.Year);
        Assert.Equal(0, transfer.LastSong.Downloads);
        Assert.Equal(0, transfer.LastSong.Comments);
        Assert.Equal(210, transfer.LastSong.SongLength);
        Assert.Equal("3:30", transfer.LastSong.FormattedTime);
        Assert.Equal("avares://ChartHub/Resources/Images/noalbumart.png", transfer.LastSong.AlbumArt);
        Assert.Equal("avares://ChartHub/Resources/Images/blankprofile.png", transfer.LastSong.Author?.AvatarPath);
        Assert.Equal("encore-user", transfer.LastSong.Author?.Name);
        Assert.Equal(0, transfer.LastSong.DrumString);
        Assert.Equal(0, transfer.LastSong.GuitarString);
        Assert.Equal(0, transfer.LastSong.BassString);
        Assert.Equal(0, transfer.LastSong.VocalString);
        Assert.Equal(0, transfer.LastSong.KeysString);
        Assert.Equal("321", transfer.LastSong.SourceId);
    }

    [Fact]
    public async Task RefreshAsync_WithAlbumArtMd5_SetsAlbumArtUrlToFilesEnchorUs()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-albumart-url");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        const string md5 = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        const string albumArtMd5 = "ffffffffffffffffffffffffffffffff";

        var handler = new RecordingHttpHandler(BuildEncoreResponseJson(
            chartId: 456,
            md5: md5,
            songOverrides: $$"""
                                    "name": "Art Song",
                                    "artist": "Art Artist",
                                    "album": "Art Album",
                                    "genre": "Pop",
                                    "year": "2023",
                                    "charter": "Art Charter",
                                    "applicationUsername": "artuser",
                                    "albumArtMd5": "{{albumArtMd5}}",
                                    "song_length": 180000,
            """));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.enchor.us"),
        };

        var api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpTransferOrchestrator(), catalog, new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();

        var song = Assert.Single(sut.DataItems);
        Assert.Equal($"https://files.enchor.us/{albumArtMd5}.jpg", song.AlbumArtUrl);
    }

    [Fact]
    public async Task RefreshAsync_WithNullAlbumArtMd5_SetsAlbumArtUrlToNoAlbumArtFallback()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-albumart-null");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        const string md5 = "aaaabbbbccccddddeeeeffffaaaabbbb";

        var handler = new RecordingHttpHandler(BuildEncoreResponseJson(chartId: 789, md5: md5));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.enchor.us"),
        };

        var api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpTransferOrchestrator(), catalog, new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();

        var song = Assert.Single(sut.DataItems);
        Assert.Equal("avares://ChartHub/Resources/Images/noalbumart.png", song.AlbumArtUrl);
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        private static readonly string EmptyResponseJson = """
            {
                "found": 0,
                "out_of": 0,
                "page": 1,
                "search_time_ms": 0,
                "data": []
            }
            """;

        private readonly string _jsonResponse;

        public RecordingHttpHandler()
            : this(EmptyResponseJson)
        {
        }

        public RecordingHttpHandler(string jsonResponse)
        {
            _jsonResponse = jsonResponse;
        }

        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.AbsolutePath ?? string.Empty);

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_jsonResponse),
            });
        }
    }

    private static string BuildEncoreResponseJson(int chartId, string md5, string? songOverrides = null)
    {
        var songJson = string.IsNullOrWhiteSpace(songOverrides)
            ? $$"""
                            "name": "Test Song",
                            "artist": "Test Artist",
                            "album": "Test Album",
                            "genre": "Rock",
                            "year": "2024",
                            "charter": "Test Charter",
                            "applicationUsername": "testuser",
                            "albumArtMd5": null,
                            "song_length": 210000,
            """
            : songOverrides;

        return $$"""
                {
                    "found": 1,
                    "out_of": 1,
                    "page": 1,
                    "search_time_ms": 0,
                    "data": [
                        {
                            {{songJson}}
                            "chartId": {{chartId}},
                            "songId": 42,
                            "groupId": 8,
                            "md5": "{{md5}}",
                            "chartHash": "chart-hash-value",
                            "versionGroupId": 10,
                            "preview_start_time": 30000,
                            "diff_band": null,
                            "diff_guitar": null,
                            "diff_bass": null,
                            "diff_drums": null,
                            "diff_vocals": null,
                            "diff_keys": null,
                            "diff_guitar_coop": null,
                            "diff_rhythm": null,
                            "diff_drums_real": null,
                            "diff_guitarghl": null,
                            "diff_bassghl": null,
                            "hasVideoBackground": true,
                            "modchart": false
                        }
                    ]
                }
                """;
    }

    private static EncoreApiService CreateApiService(LibraryCatalogService catalog, HttpClient httpClient)
    {
        var constructor = typeof(EncoreApiService).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [typeof(LibraryCatalogService), typeof(HttpClient)],
            modifiers: null);

        Assert.NotNull(constructor);
        return (EncoreApiService)constructor!.Invoke([catalog, httpClient]);
    }

    private sealed class NoOpTransferOrchestrator : ITransferOrchestrator
    {
        public Task<TransferResult> QueueSongDownloadAsync(ViewSong song, DownloadFile? downloadItem, ObservableCollection<DownloadFile> downloads, CancellationToken cancellationToken = default)
        {
            var item = downloadItem ?? new DownloadFile(song.FileName ?? "song.sng", Path.GetTempPath(), song.DownloadLink ?? string.Empty, song.FileSize)
            {
                Finished = true,
                Status = TransferStage.Completed.ToString(),
                DownloadProgress = 100,
            };
            return Task.FromResult(new TransferResult(true, TransferStage.Completed, song.FileName, null, item));
        }

        public Task<IReadOnlyList<string>> DownloadSelectedCloudFilesToLocalAsync(IEnumerable<WatcherFile> selectedCloudFiles, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> SyncCloudToLocalAdditiveAsync(IEnumerable<WatcherFile> currentCloudFiles, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class CapturingTransferOrchestrator : ITransferOrchestrator
    {
        public ViewSong? LastSong { get; private set; }

        public Task<TransferResult> QueueSongDownloadAsync(ViewSong song, DownloadFile? downloadItem, ObservableCollection<DownloadFile> downloads, CancellationToken cancellationToken = default)
        {
            LastSong = song;
            var item = downloadItem ?? new DownloadFile(song.FileName ?? "song.sng", Path.GetTempPath(), song.DownloadLink ?? string.Empty, song.FileSize)
            {
                Finished = true,
                Status = TransferStage.Completed.ToString(),
                DownloadProgress = 100,
            };

            return Task.FromResult(new TransferResult(true, TransferStage.Completed, song.FileName, null, item));
        }

        public Task<IReadOnlyList<string>> DownloadSelectedCloudFilesToLocalAsync(IEnumerable<WatcherFile> selectedCloudFiles, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> SyncCloudToLocalAdditiveAsync(IEnumerable<WatcherFile> currentCloudFiles, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class NoOpSettingsOrchestrator : ISettingsOrchestrator
    {
        public AppConfigRoot Current { get; private set; } = new();
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
