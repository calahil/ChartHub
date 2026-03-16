using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using ChartHub.Services;
using ChartHub.ViewModels;

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

        var sut = CreateService(
            configurationValues: new Dictionary<string, string?>
            {
                ["UseMockData"] = "True",
                ["rhythmverseToken"] = "token-test",
            },
            httpClient,
            loadEmbeddedMockData: () => BuildMappedSongResponseJson(),
            resolveMockDataPath: () => null,
            isAndroid: false);

        var results = await sut.GetSongFilesAsync(
            search: false,
            searchString: string.Empty,
            sort: "downloads",
            order: "desc",
            instrument: [],
            authorText: string.Empty);

        var song = Assert.Single(results);
        Assert.Equal("File Artist", song.Artist);
        Assert.Equal("File Title", song.Title);
        Assert.Equal("File Album", song.Album);
        Assert.Equal("Rock", song.Genre);
        Assert.Equal("1999", song.Year);
        Assert.Equal(10, song.Downloads);   // FileData.Downloads=10 is non-zero so it wins over DataData.Downloads=42
        Assert.Equal(7, song.Comments);
        Assert.Equal(210, song.SongLength);
        Assert.Equal("https://rhythmverse.co/img/data-art.png", song.AlbumArt);
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
    public async Task GetSongFilesAsync_WhenRuntimeUseMockDataTrue_UsesMockPayload()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("HTTP fallback should not be used when Runtime:UseMockData is enabled.")))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        var sut = CreateService(
            configurationValues: new Dictionary<string, string?>
            {
                ["Runtime:UseMockData"] = "True",
                ["rhythmverseToken"] = "token-test",
            },
            httpClient,
            loadEmbeddedMockData: () => BuildMappedSongResponseJson(),
            resolveMockDataPath: () => null,
            isAndroid: false);

        var results = await sut.GetSongFilesAsync(
            search: false,
            searchString: string.Empty,
            sort: "downloads",
            order: "desc",
            instrument: [],
            authorText: string.Empty);

        var song = Assert.Single(results);
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

        var sut = CreateService(
            configurationValues: new Dictionary<string, string?>
            {
                ["UseMockData"] = "True",
                ["rhythmverseToken"] = "token-test",
            },
            httpClient,
            loadEmbeddedMockData: () => null,
            resolveMockDataPath: () => null,
            isAndroid: false);

        var results = await sut.GetSongFilesAsync(
            search: true,
            searchString: "needle",
            sort: "downloads",
            order: "asc",
            instrument: [new InstrumentItem { Value = "guitar" }],
            authorText: "alice");

        var song = Assert.Single(results);
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
        Assert.Equal("avares://ChartHub/Resources/Images/noalbumart.png", song.AlbumArt);
        Assert.Equal("avares://ChartHub/Resources/Images/blankprofile.png", song.Author?.AvatarPath);
        Assert.Equal("https://cdn.example/live-song.zip", song.DownloadLink);
        Assert.Equal("yarg.png", song.Gameformat);
        Assert.Equal(2, song.DrumString);
    }

      [Fact]
      public async Task GetSongFilesAsync_WhenLoadingNextPage_AppendsResultsInsteadOfClearing()
      {
        using var httpClient = new HttpClient(new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(BuildMappedSongResponseJson()),
        })))
        {
          BaseAddress = new Uri("https://rhythmverse.co"),
        };

        var sut = CreateService(
          configurationValues: new Dictionary<string, string?>
          {
            ["UseMockData"] = "False",
            ["rhythmverseToken"] = "token-test",
          },
          httpClient,
          loadEmbeddedMockData: () => null,
          resolveMockDataPath: () => null,
          isAndroid: false);

        var firstPage = await sut.GetSongFilesAsync(
          search: true,
          searchString: string.Empty,
          sort: "downloads",
          order: "desc",
          instrument: [],
          authorText: string.Empty);

        sut.CurrentPage = 2;

        var secondPage = await sut.GetSongFilesAsync(
          search: false,
          searchString: string.Empty,
          sort: "downloads",
          order: "desc",
          instrument: [],
          authorText: string.Empty);

        Assert.Same(firstPage, secondPage);
        Assert.Equal(2, secondPage.Count);
        Assert.Equal(2, sut.CurrentPage);
      }

    private static ApiClientService CreateService(
        IReadOnlyDictionary<string, string?> configurationValues,
        HttpClient httpClient,
        Func<string?> loadEmbeddedMockData,
        Func<string?> resolveMockDataPath,
        bool isAndroid)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();

        var constructor = typeof(ApiClientService).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [
                typeof(IConfiguration),
                typeof(HttpClient),
                typeof(Func<string?>),
                typeof(Func<string?>),
                typeof(Func<bool>),
            ],
            modifiers: null);

        Assert.NotNull(constructor);

        return (ApiClientService)constructor.Invoke([
            configuration,
            httpClient,
            loadEmbeddedMockData,
            resolveMockDataPath,
            () => isAndroid,
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
                  "album_art": "/img/data-art.png"
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
                  "album_art": "/img/file-art.png",
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

    private static string BuildLiveFallbackResponseJson()
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
                  "download_url": "https://cdn.example/live-song.zip",
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
        var json = """{"status":"ok","data":{"records":{"total_available":1,"total_filtered":1,"returned":1},"pagination":{"start":0,"records":"1","page":"1"},"songs":[{"data":{"artist":"D","downloads":42},"file":{"file_name":"s.zip","download_url":"/x.zip","downloads":10,"file_artist":"FA"}}]}}""";
        var r = ChartHub.Models.RootResponse.FromJson(json);
        var song = r.Data.Songs[0];
        Assert.Equal(42, song.Data.DataData?.Downloads);
        Assert.Equal(10, song.File?.Downloads);
      }
    }
