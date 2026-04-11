using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Models;
using ChartHub.Services;
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

        EncoreApiService api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpChartHubServerApiClient(), new NoOpSettingsOrchestrator(), new SharedDownloadQueue())
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

        EncoreApiService api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpChartHubServerApiClient(), new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();

        Assert.Contains(handler.RequestUris, uri => uri.Contains("/search", StringComparison.Ordinal) && !uri.Contains("/search/advanced", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAsync_WhenCanonicalCatalogEntryExists_MarksSongAsInLibrary_AndUsesCanonicalSourceId()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-canonical-membership");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        const string md5 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        string canonicalSourceId = LibraryIdentityService.BuildEncoreSourceKey(777, md5);
        await catalog.UpsertAsync(new LibraryCatalogEntry(
            LibrarySourceNames.Encore,
            canonicalSourceId,
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

        EncoreApiService api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpChartHubServerApiClient(), new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();

        EncoreSong song = Assert.Single(sut.DataItems);
        Assert.True(song.IsInLibrary);
        Assert.Equal(canonicalSourceId, song.SourceId);
    }

    [Fact]
    public async Task DownloadSongAsync_DoesNotPersistInstalledLibraryEntryBeforeInstall()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-download-no-library-upsert");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        const string md5 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        var handler = new RecordingHttpHandler(BuildEncoreResponseJson(chartId: 888, md5: md5));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.enchor.us"),
        };

        EncoreApiService api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpChartHubServerApiClient(), new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();
        EncoreSong song = Assert.Single(sut.DataItems);

        await sut.DownloadSongAsync(song);

        Assert.False(await catalog.IsInLibraryAsync(LibrarySourceNames.Encore, LibraryIdentityService.BuildEncoreSourceKey(888, md5)));
        Assert.False(song.IsInLibrary);
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

        EncoreApiService api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpChartHubServerApiClient(), new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();

        EncoreSong song = Assert.Single(sut.DataItems);
        Assert.Equal(210000, song.SongLengthMs);
        Assert.Equal("3:30", song.FormattedTime);
    }

    [Fact]
    public async Task DownloadSongAsync_UsesServerJobRequestFallbacksForEncore()
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

        EncoreApiService api = CreateApiService(catalog, httpClient);
        var serverApi = new CapturingChartHubServerApiClient();
        var sut = new EncoreViewModel(api, serverApi, new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();
        EncoreSong song = Assert.Single(sut.DataItems);

        await sut.DownloadSongAsync(song);

        Assert.NotNull(serverApi.LastRequest);
        Assert.Equal(LibrarySourceNames.Encore, serverApi.LastRequest!.Source);
        Assert.Equal(LibraryIdentityService.BuildEncoreSourceKey(321, md5), serverApi.LastRequest.SourceId);
        Assert.Equal("Unknown Artist - Unknown Song.sng", serverApi.LastRequest.DisplayName);
        Assert.Equal(song.DownloadUrl, serverApi.LastRequest.SourceUrl);
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

        EncoreApiService api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpChartHubServerApiClient(), new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();

        EncoreSong song = Assert.Single(sut.DataItems);
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

        EncoreApiService api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpChartHubServerApiClient(), new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();

        EncoreSong song = Assert.Single(sut.DataItems);
        Assert.Equal("avares://ChartHub/Resources/Images/noalbumart.png", song.AlbumArtUrl);
    }

    [Fact]
    public void HasActiveDownloads_TracksSharedQueueItems()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-active-download-state");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.enchor.us"),
        };

        EncoreApiService api = CreateApiService(catalog, httpClient);
        var sharedQueue = new SharedDownloadQueue();
        var sut = new EncoreViewModel(api, new NoOpChartHubServerApiClient(), new NoOpSettingsOrchestrator(), sharedQueue);

        Assert.False(sut.HasActiveDownloads);
        Assert.True(sut.NoActiveDownloads);

        sharedQueue.Downloads.Add(new DownloadFile("Song", temp.RootPath, "https://example.test/song", 10));

        Assert.True(sut.HasActiveDownloads);
        Assert.False(sut.NoActiveDownloads);
    }

    [Fact]
    public void ClearDownloadCommand_RemovesDownloadFromSharedQueue()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-clear-download");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.enchor.us"),
        };

        EncoreApiService api = CreateApiService(catalog, httpClient);
        var sharedQueue = new SharedDownloadQueue();
        var sut = new EncoreViewModel(api, new NoOpChartHubServerApiClient(), new NoOpSettingsOrchestrator(), sharedQueue);
        var item = new DownloadFile("Song", temp.RootPath, "https://example.test/song", 10);
        sharedQueue.Downloads.Add(item);

        sut.ClearDownloadCommand.Execute(item);

        Assert.Empty(sharedQueue.Downloads);
        Assert.True(sut.NoActiveDownloads);
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
        string songJson = string.IsNullOrWhiteSpace(songOverrides)
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
        ConstructorInfo? constructor = typeof(EncoreApiService).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [typeof(LibraryCatalogService), typeof(HttpClient)],
            modifiers: null);

        Assert.NotNull(constructor);
        return (EncoreApiService)constructor!.Invoke([catalog, httpClient]);
    }

    private sealed class NoOpChartHubServerApiClient : IChartHubServerApiClient
    {
        public Task<ChartHubServerAuthExchangeResponse> ExchangeGoogleTokenAsync(string baseUrl, string googleIdToken, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChartHubServerAuthExchangeResponse("token", DateTimeOffset.UtcNow.AddHours(1)));

        public Task<ChartHubServerDownloadJobResponse> CreateDownloadJobAsync(string baseUrl, string bearerToken, ChartHubServerCreateDownloadJobRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChartHubServerDownloadJobResponse(
                Guid.NewGuid(),
                request.Source,
                request.SourceId,
                request.DisplayName,
                request.SourceUrl,
                "Queued",
                0,
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));

        public Task<IReadOnlyList<ChartHubServerDownloadJobResponse>> ListDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ChartHubServerDownloadJobResponse>>([]);

        public async IAsyncEnumerable<IReadOnlyList<ChartHubServerDownloadProgressEvent>> StreamDownloadJobsAsync(
            string baseUrl,
            string bearerToken,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }

        public Task RequestCancelDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ChartHubServerDownloadJobResponse> RequestInstallDownloadJobAsync(
            string baseUrl,
            string bearerToken,
            Guid jobId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChartHubServerDownloadJobResponse(
                jobId,
                LibrarySourceNames.Encore,
                "source-id",
                "display-name",
                "https://example.test/download",
                "Installing",
                95,
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
    }

    private sealed class CapturingChartHubServerApiClient : IChartHubServerApiClient
    {
        public ChartHubServerCreateDownloadJobRequest? LastRequest { get; private set; }

        public Task<ChartHubServerAuthExchangeResponse> ExchangeGoogleTokenAsync(string baseUrl, string googleIdToken, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChartHubServerAuthExchangeResponse("token", DateTimeOffset.UtcNow.AddHours(1)));

        public Task<ChartHubServerDownloadJobResponse> CreateDownloadJobAsync(string baseUrl, string bearerToken, ChartHubServerCreateDownloadJobRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ChartHubServerDownloadJobResponse(
                Guid.NewGuid(),
                request.Source,
                request.SourceId,
                request.DisplayName,
                request.SourceUrl,
                "Queued",
                0,
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyList<ChartHubServerDownloadJobResponse>> ListDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ChartHubServerDownloadJobResponse>>([]);

        public async IAsyncEnumerable<IReadOnlyList<ChartHubServerDownloadProgressEvent>> StreamDownloadJobsAsync(
            string baseUrl,
            string bearerToken,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }

        public Task RequestCancelDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ChartHubServerDownloadJobResponse> RequestInstallDownloadJobAsync(
            string baseUrl,
            string bearerToken,
            Guid jobId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChartHubServerDownloadJobResponse(
                jobId,
                LibrarySourceNames.Encore,
                "source-id",
                "display-name",
                "https://example.test/download",
                "Installing",
                95,
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
    }

    private sealed class NoOpSettingsOrchestrator : ISettingsOrchestrator
    {
        public AppConfigRoot Current { get; private set; } = new()
        {
            Runtime = new RuntimeAppConfig
            {
                ServerApiBaseUrl = "http://127.0.0.1:5001",
                ServerApiAuthToken = "sync-token-test",
            },
        };
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
