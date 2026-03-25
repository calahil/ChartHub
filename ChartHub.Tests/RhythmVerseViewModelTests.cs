using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Reflection;

using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Services.Transfers;
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
        ConstructorInfo? constructor = typeof(RhythmVerseViewModel).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(ApiClientService),
                typeof(ITransferOrchestrator),
                typeof(LibraryCatalogService),
                typeof(SharedDownloadQueue),
                typeof(bool),
                typeof(Func<Action, Task>),
            ],
            modifiers: null);

        Assert.NotNull(constructor);

        return (RhythmVerseViewModel)constructor.Invoke([
            apiClient,
            new NoOpTransferOrchestrator(),
            catalog,
            sharedQueue,
            false,
            (Func<Action, Task>)(action => { action(); return Task.CompletedTask; }),
        ]);
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

    private sealed class NoOpTransferOrchestrator : ITransferOrchestrator
    {
        public Task<TransferResult> QueueSongDownloadAsync(ViewSong song, DownloadFile? downloadItem, ObservableCollection<DownloadFile> downloads, CancellationToken cancellationToken = default)
        {
            DownloadFile item = downloadItem ?? new DownloadFile(song.FileName ?? "song.sng", Path.GetTempPath(), song.DownloadLink ?? string.Empty, song.FileSize)
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
}
