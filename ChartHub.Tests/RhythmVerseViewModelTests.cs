using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.ViewModels;

using Microsoft.Extensions.Configuration;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class RhythmVerseViewModelTests
{
    [Fact]
    public void HasActiveDownloads_TracksSharedQueueItems()
    {
        using var temp = new TemporaryDirectoryFixture("rhythmverse-vm-active-download-state");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sharedQueue = new SharedDownloadQueue();
        RhythmVerseViewModel sut = CreateViewModelForStateTests(catalog, sharedQueue);

        Assert.False(sut.HasActiveDownloads);
        Assert.True(sut.NoActiveDownloads);

        sharedQueue.Downloads.Add(new DownloadFile("Song", temp.RootPath, "https://example.test/song", 10));

        Assert.True(sut.HasActiveDownloads);
        Assert.False(sut.NoActiveDownloads);
    }

    [Fact]
    public void ClearDownloadCommand_RemovesDownloadFromSharedQueue()
    {
        using var temp = new TemporaryDirectoryFixture("rhythmverse-vm-clear-download");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sharedQueue = new SharedDownloadQueue();
        RhythmVerseViewModel sut = CreateViewModelForStateTests(catalog, sharedQueue);
        var item = new DownloadFile("Song", temp.RootPath, "https://example.test/song", 10);
        sharedQueue.Downloads.Add(item);

        sut.ClearDownloadCommand.Execute(item);

        Assert.Empty(sharedQueue.Downloads);
        Assert.True(sut.NoActiveDownloads);
    }

    [Fact]
    public void SuccessfulDownloadStatus_AutoClearsAfterDelay()
    {
        using var temp = new TemporaryDirectoryFixture("rhythmverse-vm-success-autoclear");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sharedQueue = new SharedDownloadQueue();
        RhythmVerseViewModel sut = CreateViewModelForStateTests(catalog, sharedQueue);
        var item = new DownloadFile("Song", temp.RootPath, "https://example.test/song", 10)
        {
            Status = "Queued",
        };

        sharedQueue.Downloads.Add(item);
        item.Status = "Completed";
        item.ErrorMessage = null;

        bool removed = SpinWait.SpinUntil(() => !sharedQueue.Downloads.Contains(item), TimeSpan.FromSeconds(5));

        Assert.True(removed);
        Assert.True(sut.NoActiveDownloads);
    }

    [Fact]
    public async Task FailedDownloadStatus_RemainsUntilManualClear()
    {
        using var temp = new TemporaryDirectoryFixture("rhythmverse-vm-failure-retained");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sharedQueue = new SharedDownloadQueue();
        RhythmVerseViewModel sut = CreateViewModelForStateTests(catalog, sharedQueue);
        var item = new DownloadFile("Song", temp.RootPath, "https://example.test/song", 10)
        {
            Status = "Failed",
            ErrorMessage = "network",
        };

        sharedQueue.Downloads.Add(item);
        await Task.Delay(TimeSpan.FromSeconds(4));

        Assert.Contains(item, sharedQueue.Downloads);

        sut.ClearDownloadCommand.Execute(item);
        Assert.Empty(sharedQueue.Downloads);
    }

    [Fact]
    public void DownloadFile_DownloadedStatus_IsClearableAndNotCancelable()
    {
        var item = new DownloadFile("Song", Path.GetTempPath(), "https://example.test/song", 10)
        {
            Status = "Downloaded",
            Finished = false,
        };

        Assert.True(item.CanClear);
        Assert.False(item.CanCancel);
    }

    [Fact]
    public async Task LoadDataAsync_ThenLoadMoreAsync_AppendsPageResults()
    {
        using var temp = new TemporaryDirectoryFixture("rhythmverse-vm-pagination-append");

        ApiClientService apiClient = CreateApiClientWithPagedHandler();
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sharedQueue = new SharedDownloadQueue();
        RhythmVerseViewModel sut = CreateViewModelForPaging(apiClient, catalog, sharedQueue);

        await sut.LoadDataAsync(search: true);
        await sut.LoadMoreAsync();

        Assert.NotNull(sut.DataItems);
        Assert.Equal(2, sut.DataItems!.Count);
        Assert.Equal(2, sut.CurrentPage);
    }

    [Fact]
    public async Task LoadMoreAsync_WhenTriggeredConcurrently_DeduplicatesPageAdvance()
    {
        using var temp = new TemporaryDirectoryFixture("rhythmverse-vm-pagination-dedup");

        ApiClientService apiClient = CreateApiClientWithPagedHandler(delayMs: 40);
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sharedQueue = new SharedDownloadQueue();
        RhythmVerseViewModel sut = CreateViewModelForPaging(apiClient, catalog, sharedQueue);

        await sut.LoadDataAsync(search: true);

        await Task.WhenAll(sut.LoadMoreAsync(), sut.LoadMoreAsync());

        Assert.NotNull(sut.DataItems);
        Assert.Equal(2, sut.DataItems!.Count);
        Assert.Equal(2, sut.CurrentPage);
    }

    [Fact]
    public async Task DownloadFile_WhenInvokedForDifferentSongs_QueuesDistinctServerRequests()
    {
        using var temp = new TemporaryDirectoryFixture("rhythmverse-vm-distinct-download-requests");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sharedQueue = new SharedDownloadQueue();
        var serverApi = new CapturingChartHubServerApiClient();
        RhythmVerseViewModel sut = CreateViewModelForPaging(
            CreateApiClientWithPagedHandler(),
            catalog,
            sharedQueue,
            new ConnectedSettingsOrchestrator(),
            serverApi);

        var first = new ViewSong
        {
            SourceId = "rv-file-1",
            Title = "First Song",
            FileName = "first-song.zip",
            DownloadLink = "https://example.test/first.zip",
            FileSize = 111,
        };
        var second = new ViewSong
        {
            SourceId = "rv-file-2",
            Title = "Second Song",
            FileName = "second-song.zip",
            DownloadLink = "https://example.test/second.zip",
            FileSize = 222,
        };

        await sut.DownloadFile(first);
        await sut.DownloadFile(second);

        Assert.Equal(2, serverApi.Requests.Count);
        Assert.Equal("rv-file-1", serverApi.Requests[0].SourceId);
        Assert.Equal("First Song", serverApi.Requests[0].DisplayName);
        Assert.Equal("https://example.test/first.zip", serverApi.Requests[0].SourceUrl);
        Assert.Equal("rv-file-2", serverApi.Requests[1].SourceId);
        Assert.Equal("Second Song", serverApi.Requests[1].DisplayName);
        Assert.Equal("https://example.test/second.zip", serverApi.Requests[1].SourceUrl);
    }

    [Fact]
    public async Task DownloadFile_WhenSongParameterIsNull_DoesNotQueueSelectedFile()
    {
        using var temp = new TemporaryDirectoryFixture("rhythmverse-vm-null-download-parameter");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sharedQueue = new SharedDownloadQueue();
        var serverApi = new CapturingChartHubServerApiClient();
        RhythmVerseViewModel sut = CreateViewModelForPaging(
            CreateApiClientWithPagedHandler(),
            catalog,
            sharedQueue,
            new ConnectedSettingsOrchestrator(),
            serverApi);

        sut.SelectedFile = new ViewSong
        {
            SourceId = "rv-file-selected",
            Title = "Selected Song",
            FileName = "selected-song.zip",
            DownloadLink = "https://example.test/selected.zip",
            FileSize = 333,
        };

        await sut.DownloadFile(null);

        Assert.Empty(serverApi.Requests);
    }

    private static RhythmVerseViewModel CreateViewModelForStateTests(
        LibraryCatalogService catalog,
        SharedDownloadQueue sharedQueue)
    {
        return CreateViewModelForPaging(CreateApiClientWithPagedHandler(), catalog, sharedQueue);
    }

    private static RhythmVerseViewModel CreateViewModelForPaging(
        ApiClientService apiClient,
        LibraryCatalogService catalog,
        SharedDownloadQueue sharedQueue)
    {
        return CreateViewModelForPaging(
            apiClient,
            catalog,
            sharedQueue,
            new FakeSettingsOrchestrator(),
            new FakeChartHubServerApiClient());
    }

    private static RhythmVerseViewModel CreateViewModelForPaging(
        ApiClientService apiClient,
        LibraryCatalogService catalog,
        SharedDownloadQueue sharedQueue,
        ISettingsOrchestrator settingsOrchestrator,
        IChartHubServerApiClient serverApiClient)
    {
        ConstructorInfo? constructor = typeof(RhythmVerseViewModel).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(ApiClientService),
                typeof(LibraryCatalogService),
                typeof(SharedDownloadQueue),
                typeof(ISettingsOrchestrator),
                typeof(IChartHubServerApiClient),
                typeof(bool),
                typeof(Func<Action, Task>),
            ],
            modifiers: null);

        Assert.NotNull(constructor);

        return (RhythmVerseViewModel)constructor.Invoke([
            apiClient,
            catalog,
            sharedQueue,
            settingsOrchestrator,
            serverApiClient,
            false,
            (Func<Action, Task>)(action => { action(); return Task.CompletedTask; }),
        ]);
    }

    private sealed class ConnectedSettingsOrchestrator : ISettingsOrchestrator
    {
        public AppConfigRoot Current { get; } = new()
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

    private sealed class FakeSettingsOrchestrator : ISettingsOrchestrator
    {
        public AppConfigRoot Current { get; } = new()
        {
            Runtime = new RuntimeAppConfig(),
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

    private sealed class FakeChartHubServerApiClient : IChartHubServerApiClient
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
                LibrarySourceNames.RhythmVerse,
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
        public List<ChartHubServerCreateDownloadJobRequest> Requests { get; } = [];

        public Task<ChartHubServerAuthExchangeResponse> ExchangeGoogleTokenAsync(string baseUrl, string googleIdToken, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChartHubServerAuthExchangeResponse("token", DateTimeOffset.UtcNow.AddHours(1)));

        public Task<ChartHubServerDownloadJobResponse> CreateDownloadJobAsync(string baseUrl, string bearerToken, ChartHubServerCreateDownloadJobRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
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
                LibrarySourceNames.RhythmVerse,
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

    private static ApiClientService CreateApiClientWithPagedHandler(int delayMs = 0)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseMockData"] = "False",
                ["rhythmverseToken"] = "token-test",
            })
            .Build();

        var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }

            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            int page = ParsePage(body);
            string json = BuildPagedResponseJson(page);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            };
        }))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        ConstructorInfo? constructor = typeof(ApiClientService).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(IConfiguration),
                typeof(HttpClient),
                typeof(Func<string?>),
                typeof(Func<string?>),
            ],
            modifiers: null);

        Assert.NotNull(constructor);

        return (ApiClientService)constructor.Invoke([
            configuration,
            httpClient,
            (Func<string?>)(() => null),
            (Func<string?>)(() => null),
        ]);
    }

    private static int ParsePage(string formBody)
    {
        foreach (string segment in formBody.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = segment.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            if (!string.Equals(parts[0], "page", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(Uri.UnescapeDataString(parts[1]), out int page))
            {
                return page;
            }
        }

        return 1;
    }

    private static string BuildPagedResponseJson(int page)
    {
        int start = Math.Max(0, (page - 1) * 25);
        return $$"""
        {
          "status": "ok",
          "data": {
            "records": {
              "total_available": 100,
              "total_filtered": 100,
              "returned": 1
            },
            "pagination": {
              "start": {{start}},
              "records": "25",
              "page": "{{page}}"
            },
            "songs": [
              {
                "data": {
                  "artist": "Artist {{page}}",
                  "title": "Title {{page}}",
                  "album": "Album {{page}}",
                  "song_length": 180,
                  "genre": "Rock",
                  "year": 2024,
                  "downloads": 10,
                  "diff_drums": "1",
                  "diff_guitar": "1",
                  "diff_bass": "1",
                  "diff_vocals": "1",
                  "diff_keys": "1",
                  "album_art": "/img/p{{page}}.png"
                },
                "file": {
                  "file_id": "rv-file-{{page}}",
                  "file_name": "song-{{page}}.zip",
                  "filename": "song-{{page}}.zip",
                  "size": 1000,
                  "downloads": 10,
                  "comments": 1,
                  "song_length": 180,
                  "file_artist": "Artist {{page}}",
                  "file_title": "Title {{page}}",
                  "file_album": "Album {{page}}",
                  "file_genre": "Rock",
                  "file_year": 2024,
                  "album_art": "/img/p{{page}}.png",
                  "download_url": "/downloads/song-{{page}}.zip",
                  "gameformat": "rb3",
                  "author": {
                    "name": "Author {{page}}",
                    "avatar_path": "/avatars/a{{page}}.png"
                  },
                  "diff_drums": 1,
                  "diff_guitar": 1,
                  "diff_bass": 1,
                  "diff_vocals": 1,
                  "diff_keys": 1
                }
              }
            ]
          }
        }
        """;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return sendAsync(request, cancellationToken);
        }
    }

}
