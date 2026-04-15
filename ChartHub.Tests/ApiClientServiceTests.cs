using System.Net;
using System.Net.Http;
using System.Reflection;

using ChartHub.Configuration.Models;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.ViewModels;

using Microsoft.Extensions.Configuration;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class ApiClientServiceTests
{
    [Fact]
    public async Task GetSongFilesAsync_WithMockPayload_MapsSongFields_AndSkipsMarketplaceEntries()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP fallback should not be used when mock payload is available.")))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        ApiClientService sut = CreateService(
            configurationValues: new Dictionary<string, string?>
            {
                ["UseMockData"] = "True",
                ["rhythmverseToken"] = "token-test",
            },
            httpClient,
            loadEmbeddedMockData: () => BuildMappedSongResponseJson(),
          resolveMockDataPath: () => null);

        IReadOnlyList<ViewSong> results = await sut.GetSongFilesAsync(
            search: false,
            searchString: string.Empty,
            sort: "downloads",
            order: "desc",
            instrument: [],
            authorText: string.Empty);

        ViewSong song = Assert.Single(results);
        Assert.Equal("File Artist", song.Artist);
        Assert.Equal("File Title", song.Title);
        Assert.Equal("File Album", song.Album);
        Assert.Equal("Rock", song.Genre);
        Assert.Equal("1999", song.Year);
        Assert.Equal(10, song.Downloads);   // FileData.Downloads=10 is non-zero so it wins over DataData.Downloads=42
        Assert.Equal(7, song.Comments);
        Assert.Equal(210, song.SongLength);
        Assert.Equal("https://rhythmverse.co/assets/album_art/data-art.png", song.AlbumArt);
        Assert.Equal("https://rhythmverse.co/downloads/data-song.zip", song.DownloadLink);
        Assert.Equal("https://rhythmverse.co/avatars/author.png", song.Author?.AvatarPath);
        Assert.Equal("rb3.png", song.Gameformat);
        Assert.Equal(4, song.DrumString);
        Assert.Equal(3, song.GuitarString);
        Assert.Equal(2, song.BassString);
        Assert.Equal(1, song.VocalString);
        Assert.Equal(LibrarySourceNames.RhythmVerse, song.SourceName);
        Assert.Equal("rv-file-123", song.SourceId);
        Assert.Equal(1, sut.CurrentPage);
        Assert.Equal(2, sut.TotalResults);
        Assert.Equal(1, sut.TotalPages);
        Assert.Equal(1, sut.StartRecord);
        Assert.Equal(25, sut.EndRecord);
    }

    [Fact]
    public async Task GetSongFilesAsync_WithMirrorSource_UsesMirrorBaseUrlForRelativeLinks()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP fallback should not be used when mock payload is available.")))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        ApiClientService sut = CreateService(
          configurationValues: new Dictionary<string, string?>
          {
              ["Runtime:UseMockData"] = "True",
              ["Runtime:RhythmVerseSource"] = nameof(RhythmVerseSource.ChartHubMirror),
              ["rhythmverseToken"] = "token-test",
          },
          httpClient,
          loadEmbeddedMockData: () => BuildMappedSongResponseJson(),
          resolveMockDataPath: () => null);

        IReadOnlyList<ViewSong> results = await sut.GetSongFilesAsync(
          search: false,
          searchString: string.Empty,
          sort: "downloads",
          order: "desc",
          instrument: [],
          authorText: string.Empty);

        ViewSong song = Assert.Single(results);
        Assert.Equal("http://protail/assets/album_art/data-art.png", song.AlbumArt);
        Assert.Equal("http://protail/downloads/data-song.zip", song.DownloadLink);
        Assert.Equal("http://protail/avatars/author.png", song.Author?.AvatarPath);
    }

    [Fact]
    public async Task GetSongFilesAsync_WhenRuntimeUseMockDataTrue_UsesMockPayload()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("HTTP fallback should not be used when Runtime:UseMockData is enabled.")))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        ApiClientService sut = CreateService(
            configurationValues: new Dictionary<string, string?>
            {
                ["Runtime:UseMockData"] = "True",
                ["rhythmverseToken"] = "token-test",
            },
            httpClient,
            loadEmbeddedMockData: () => BuildMappedSongResponseJson(),
          resolveMockDataPath: () => null);

        IReadOnlyList<ViewSong> results = await sut.GetSongFilesAsync(
            search: false,
            searchString: string.Empty,
            sort: "downloads",
            order: "desc",
            instrument: [],
            authorText: string.Empty);

        ViewSong song = Assert.Single(results);
        Assert.Equal("File Artist", song.Artist);
    }

    [Fact]
    public async Task GetSongFilesAsync_WhenMockDataUnavailable_FallsBackToLiveApi_AndMapsFileFields()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            capturedRequest = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildLiveFallbackResponseJson()),
            };
        }))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        ApiClientService sut = CreateService(
            configurationValues: new Dictionary<string, string?>
            {
                ["UseMockData"] = "True",
                ["rhythmverseToken"] = "token-test",
            },
            httpClient,
            loadEmbeddedMockData: () => null,
          resolveMockDataPath: () => null);

        IReadOnlyList<ViewSong> results = await sut.GetSongFilesAsync(
            search: true,
            searchString: "needle",
            sort: "downloads",
            order: "asc",
            instrument: [new InstrumentItem { Value = "guitar" }],
            authorText: "alice");

        ViewSong song = Assert.Single(results);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("/api/all/songfiles/search/live", capturedRequest.RequestUri!.AbsolutePath);
        Assert.NotNull(capturedBody);
        Assert.Contains("instrument=guitar", capturedBody, StringComparison.Ordinal);
        Assert.Contains("author=alice", capturedBody, StringComparison.Ordinal);
        Assert.Contains("text=needle", capturedBody, StringComparison.Ordinal);
        Assert.Contains("page=1", capturedBody, StringComparison.Ordinal);
        Assert.Contains("records=25", capturedBody, StringComparison.Ordinal);
        Assert.Equal("Live Artist", song.Artist);
        Assert.Equal("Live Title", song.Title);
        Assert.Equal("Live Album", song.Album);
        Assert.Equal("Pop", song.Genre);
        Assert.Equal("2024", song.Year);
        Assert.Equal(12, song.Downloads);
        Assert.Equal(2, song.Comments);
        Assert.Equal(180, song.SongLength);
        Assert.Equal("avares://ChartHub/Resources/Images/noalbumart.svg", song.AlbumArt);
        Assert.Equal("avares://ChartHub/Resources/Images/blankprofile.svg", song.Author?.AvatarPath);
        Assert.Equal("https://cdn.example/live-song.zip", song.DownloadLink);
        Assert.Equal("yarg.png", song.Gameformat);
        Assert.Equal(2, song.DrumString);
    }

    [Fact]
    public async Task GetSongFilesAsync_WithMirrorSource_RewritesAbsoluteExternalDownloadUrlToMirrorProxy()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(BuildLiveFallbackResponseJson()),
        }))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        ApiClientService sut = CreateService(
            configurationValues: new Dictionary<string, string?>
            {
                ["Runtime:UseMockData"] = "False",
                ["Runtime:RhythmVerseSource"] = nameof(RhythmVerseSource.ChartHubMirror),
                ["rhythmverseToken"] = "token-test",
            },
            httpClient,
            loadEmbeddedMockData: () => null,
            resolveMockDataPath: () => null);

        IReadOnlyList<ViewSong> results = await sut.GetSongFilesAsync(
            search: true,
            searchString: string.Empty,
            sort: "downloads",
            order: "desc",
            instrument: [],
            authorText: string.Empty);

        ViewSong song = Assert.Single(results);
        Assert.Equal("http://backupapi.protail/downloads/external?sourceUrl=https%3A%2F%2Fcdn.example%2Flive-song.zip", song.DownloadLink);
    }

    [Fact]
    public async Task GetSongFilesAsync_WithMirrorSource_DoesNotRewriteGoogleDriveDownloadUrl()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(BuildLiveFallbackResponseJson("https://drive.google.com/file/d/file-123/view")),
        }))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        ApiClientService sut = CreateService(
            configurationValues: new Dictionary<string, string?>
            {
                ["Runtime:UseMockData"] = "False",
                ["Runtime:RhythmVerseSource"] = nameof(RhythmVerseSource.ChartHubMirror),
                ["rhythmverseToken"] = "token-test",
            },
            httpClient,
            loadEmbeddedMockData: () => null,
            resolveMockDataPath: () => null);

        IReadOnlyList<ViewSong> results = await sut.GetSongFilesAsync(
            search: true,
            searchString: string.Empty,
            sort: "downloads",
            order: "desc",
            instrument: [],
            authorText: string.Empty);

        ViewSong song = Assert.Single(results);
        Assert.Equal("https://drive.google.com/file/d/file-123/view", song.DownloadLink);
    }

    [Fact]
    public async Task GetSongFilesAsync_WithMirrorSource_DoesNotRewriteMediaFireDownloadUrl()
    {
        const string mediaFireUrl = "https://www.mediafire.com/file/abc123/sample/file";

        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(BuildLiveFallbackResponseJson(downloadUrl: mediaFireUrl)),
        }))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        ApiClientService sut = CreateService(
            configurationValues: new Dictionary<string, string?>
            {
                ["Runtime:UseMockData"] = "False",
                ["Runtime:RhythmVerseSource"] = nameof(RhythmVerseSource.ChartHubMirror),
                ["rhythmverseToken"] = "token-test",
            },
            httpClient,
            loadEmbeddedMockData: () => null,
            resolveMockDataPath: () => null);

        IReadOnlyList<ViewSong> results = await sut.GetSongFilesAsync(
            search: true,
            searchString: string.Empty,
            sort: "downloads",
            order: "desc",
            instrument: [],
            authorText: string.Empty);

        ViewSong song = Assert.Single(results);
        Assert.Equal(mediaFireUrl, song.DownloadLink);
    }

    [Fact]
    public async Task GetSongFilesAsync_WithMirrorSource_WhenDownloadUrlIsMalformedExternalProxy_UsesDownloadPageUrlFull()
    {
        const string malformedProxyUrl = "http://backupapi.protail/downloads/external";
        const string mediaFireUrl = "https://www.mediafire.com/file/abc123/sample/file";

        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(BuildLiveFallbackResponseJson(
                downloadUrl: malformedProxyUrl,
                downloadPageUrl: "/download/abc123",
                downloadPageUrlFull: mediaFireUrl)),
        }))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        ApiClientService sut = CreateService(
            configurationValues: new Dictionary<string, string?>
            {
                ["Runtime:UseMockData"] = "False",
                ["Runtime:RhythmVerseSource"] = nameof(RhythmVerseSource.ChartHubMirror),
                ["rhythmverseToken"] = "token-test",
            },
            httpClient,
            loadEmbeddedMockData: () => null,
            resolveMockDataPath: () => null);

        IReadOnlyList<ViewSong> results = await sut.GetSongFilesAsync(
            search: true,
            searchString: string.Empty,
            sort: "downloads",
            order: "desc",
            instrument: [],
            authorText: string.Empty);

        ViewSong song = Assert.Single(results);
        Assert.Equal(mediaFireUrl, song.DownloadLink);
    }

    [Fact]
    public async Task GetSongFilesAsync_OnAndroid_WhenUseMockDataFalse_UsesLiveApi()
    {
        HttpRequestMessage? capturedRequest = null;

        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildLiveFallbackResponseJson()),
            };
        }))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        ApiClientService sut = CreateService(
            configurationValues: new Dictionary<string, string?>
            {
                ["Runtime:UseMockData"] = "False",
                ["rhythmverseToken"] = "token-test",
            },
            httpClient,
            loadEmbeddedMockData: () => throw new InvalidOperationException("Mock data should not be loaded when UseMockData is false."),
          resolveMockDataPath: () => throw new InvalidOperationException("Mock data path should not be resolved when UseMockData is false."));

        IReadOnlyList<ViewSong> results = await sut.GetSongFilesAsync(
            search: true,
            searchString: string.Empty,
            sort: "downloads",
            order: "desc",
            instrument: [],
            authorText: string.Empty);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        ViewSong song = Assert.Single(results);
        Assert.Equal("Live Artist", song.Artist);
    }

    [Fact]
    public async Task GetSongFilesAsync_WhenLoadingNextPage_ReturnsCurrentPageDataOnly()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(BuildMappedSongResponseJson()),
        })))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        ApiClientService sut = CreateService(
          configurationValues: new Dictionary<string, string?>
          {
              ["UseMockData"] = "False",
              ["rhythmverseToken"] = "token-test",
          },
          httpClient,
          loadEmbeddedMockData: () => null,
          resolveMockDataPath: () => null);

        IReadOnlyList<ViewSong> firstPage = await sut.GetSongFilesAsync(
          search: true,
          searchString: string.Empty,
          sort: "downloads",
          order: "desc",
          instrument: [],
          authorText: string.Empty);

        sut.CurrentPage = 2;

        IReadOnlyList<ViewSong> secondPage = await sut.GetSongFilesAsync(
          search: false,
          searchString: string.Empty,
          sort: "downloads",
          order: "desc",
          instrument: [],
          authorText: string.Empty);

        Assert.NotSame(firstPage, secondPage);
        Assert.Single(firstPage);
        Assert.Single(secondPage);
        Assert.Equal(2, sut.CurrentPage);
    }

    private static ApiClientService CreateService(
        IReadOnlyDictionary<string, string?> configurationValues,
        HttpClient httpClient,
        Func<string?> loadEmbeddedMockData,
      Func<string?> resolveMockDataPath)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();

        ConstructorInfo? constructor = typeof(ApiClientService).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
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
            loadEmbeddedMockData,
            resolveMockDataPath,
        ]);
    }

    private static string BuildMappedSongResponseJson()
    {
        return """
        {
          "status": "ok",
          "data": {
            "records": {
              "total_available": 2,
              "total_filtered": 2,
              "returned": 2
            },
            "pagination": {
              "start": 0,
              "records": "25",
              "page": "1"
            },
            "songs": [
              {
                "data": {
                  "artist": "Data Artist",
                  "title": "Data Title",
                  "album": "Data Album",
                  "song_length": 210,
                  "genre": "Rock",
                  "year": 1999,
                  "downloads": 42,
                  "diff_drums": "4",
                  "diff_guitar": "3",
                  "diff_bass": "2",
                  "diff_vocals": "1",
                  "diff_keys": "1",
                  "album_art": "/assets/album_art/data-art.png"
                },
                "file": {
                  "file_id": "rv-file-123",
                  "file_name": "data-song.zip",
                  "filename": "fallback-name.zip",
                  "size": 1234,
                   "downloads": 10,
                  "comments": 7,
                  "song_length": 200,
                  "file_song_length": 190,
                  "file_artist": "File Artist",
                  "file_title": "File Title",
                  "file_album": "File Album",
                  "file_genre": "File Genre",
                  "file_year": 1980,
                  "album_art": "/assets/album_art/file-art.png",
                  "download_url": "/downloads/data-song.zip",
                  "gameformat": "rb3",
                  "author": {
                    "name": "Author One",
                    "avatar_path": "/avatars/author.png"
                  },
                  "diff_drums": 1,
                  "diff_guitar": 2,
                  "diff_bass": 1,
                  "diff_vocals": 0,
                  "diff_keys": 0
                }
              },
              {
                "data": false,
                "file": {
                  "file_name": "marketplace-song.zip",
                  "filename": "marketplace-song.zip",
                  "download_url": "http://marketplace.xbox.com/en-US/Product/foo",
                  "author": {
                    "name": "Author Two"
                  }
                }
              }
            ]
          }
        }
        """;
    }

    private static string BuildLiveFallbackResponseJson(
      string downloadUrl = "https://cdn.example/live-song.zip",
      string downloadPageUrl = "/download/live-song",
      string downloadPageUrlFull = "https://rhythmverse.co/download/live-song")
    {
        return """
        {
          "status": "ok",
          "data": {
            "records": {
              "total_available": 1,
              "total_filtered": 1,
              "returned": 1
            },
            "pagination": {
              "start": 0,
              "records": "25",
              "page": "1"
            },
            "songs": [
              {
                "data": false,
                "file": {
                  "file_name": "live-song.zip",
                  "filename": "live-song.zip",
                  "size": 555,
                  "downloads": 12,
                  "comments": 2,
                  "song_length": 0,
                  "file_song_length": 180,
                  "file_artist": "Live Artist",
                  "file_title": "Live Title",
                  "file_album": "Live Album",
                  "file_genre": "Pop",
                  "file_year": 2024,
                  "album_art": "",
                  "download_url": "__DOWNLOAD_URL__",
                  "download_page_url": "__DOWNLOAD_PAGE_URL__",
                  "download_page_url_full": "__DOWNLOAD_PAGE_URL_FULL__",
                  "gameformat": "yarg",
                  "author": {
                    "name": "Live Author",
                    "avatar_path": null
                  },
                  "diff_drums": 2
                }
              }
            ]
          }
        }
        """
        .Replace("__DOWNLOAD_URL__", downloadUrl, StringComparison.Ordinal)
        .Replace("__DOWNLOAD_PAGE_URL__", downloadPageUrl, StringComparison.Ordinal)
        .Replace("__DOWNLOAD_PAGE_URL_FULL__", downloadPageUrlFull, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSongFilesAsync_WithDifficultiesAsObjects_ParsesWithoutException()
    {
        // Regression: guitarghl and guitar_coop inside difficulties can be DrumsClass objects,
        // not just empty arrays. The model previously declared them as List<object> without
        // DrumsClassOrListConverter, causing a JsonException on real RhythmVerse payloads.
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP fallback should not be used when mock payload is available.")))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        ApiClientService sut = CreateService(
            configurationValues: new Dictionary<string, string?>
            {
                ["UseMockData"] = "True",
                ["rhythmverseToken"] = "token-test",
            },
            httpClient,
            loadEmbeddedMockData: () => BuildDifficultiesAsObjectsResponseJson(),
            resolveMockDataPath: () => null);

        IReadOnlyList<ViewSong> results = await sut.GetSongFilesAsync(
            search: false,
            searchString: string.Empty,
            sort: "downloads",
            order: "desc",
            instrument: [],
            authorText: string.Empty);

        Assert.Single(results);
        Assert.Equal("GHL Artist", results[0].Artist);
    }

    private static string BuildDifficultiesAsObjectsResponseJson()
    {
        return """
        {
          "status": "ok",
          "data": {
            "records": { "total_available": 1, "total_filtered": 1, "returned": 1 },
            "pagination": { "start": 0, "records": "25", "page": "1" },
            "songs": [
              {
                "data": false,
                "file": {
                  "file_id": "ghl-file-1",
                  "file_name": "ghl-song.zip",
                  "file_artist": "GHL Artist",
                  "file_title": "GHL Title",
                  "download_url": "https://example.com/ghl-song.zip",
                  "gameformat": "ch",
                  "author": { "name": "Charter" },
                  "difficulties": {
                    "guitarghl": { "e": 1, "m": 2, "h": 3, "x": 4, "all": 4 },
                    "guitar_coop": { "e": 0, "m": 0, "h": 1, "x": 2, "all": 2 }
                  }
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


[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class ApiClientServiceParsingTests
{
    [Fact]
    public void JsonParsing_VerifyFileDownloadsField()
    {
        string json = """{"status":"ok","data":{"records":{"total_available":1,"total_filtered":1,"returned":1},"pagination":{"start":0,"records":"1","page":"1"},"songs":[{"data":{"artist":"D","downloads":42},"file":{"file_name":"s.zip","download_url":"/x.zip","downloads":10,"file_artist":"FA"}}]}}""";
        var r = ChartHub.Models.RootResponse.FromJson(json);
        Song song = r.Data.Songs[0];
        Assert.Equal(42, song.Data.DataData?.Downloads);
        Assert.Equal(10, song.File?.Downloads);
    }
}
