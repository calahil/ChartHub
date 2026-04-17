using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

using ChartHub.BackupApi.Models;
using ChartHub.BackupApi.Options;
using ChartHub.BackupApi.Services;
using ChartHub.BackupApi.Tests.TestInfrastructure;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class UpstreamClientConversionTests
{
    [Fact]
    public async Task FetchAndConvert_WithTestFixture_MapsFirstSongFieldsCorrectly()
    {
        string json = await File.ReadAllTextAsync("test.json");
        RhythmVerseUpstreamClient sut = BuildClient(json);

        RhythmVersePageEnvelope envelope = await sut.FetchSongsPageAsync(1, 25, null, CancellationToken.None);
        IReadOnlyList<SyncedSong> songs = sut.ConvertToSyncedSongs(envelope);

        Assert.True(songs.Count > 0);

        // First song in test.json is "Sweet Child o' Mine" by Guns N' Roses
        SyncedSong first = songs[0];
        Assert.Equal(240L, first.SongId);
        Assert.Equal("sweet-child-o39-mine-240", first.RecordId);
        Assert.Equal("Guns N' Roses", first.Artist);
        Assert.Equal("Sweet Child o' Mine", first.Title);
        Assert.Equal("Appetite for Destruction", first.Album);
        Assert.Equal("Metal", first.Genre);
        Assert.Equal(1987, first.Year);
        Assert.Equal(1760786929L, first.RecordUpdatedUnix);
    }

    [Fact]
    public async Task FetchAndConvert_WithTestFixture_MapsPhase2DiffFields()
    {
        string json = await File.ReadAllTextAsync("test.json");
        RhythmVerseUpstreamClient sut = BuildClient(json);

        RhythmVersePageEnvelope envelope = await sut.FetchSongsPageAsync(1, 25, null, CancellationToken.None);
        IReadOnlyList<SyncedSong> songs = sut.ConvertToSyncedSongs(envelope);

        SyncedSong first = songs[0];

        // data.diff_* is stored as strings ("4", "7", etc.) — ReadNullableInt handles both
        Assert.Equal(7, first.DiffGuitar);
        Assert.Equal(5, first.DiffBass);
        Assert.Equal(4, first.DiffDrums);
        Assert.Equal(7, first.DiffVocals);
        Assert.Equal(6, first.DiffBand);
    }

    [Fact]
    public async Task FetchAndConvert_WithTestFixture_MapsAuthorGroupAndGameFormat()
    {
        string json = await File.ReadAllTextAsync("test.json");
        RhythmVerseUpstreamClient sut = BuildClient(json);

        RhythmVersePageEnvelope envelope = await sut.FetchSongsPageAsync(1, 25, null, CancellationToken.None);
        IReadOnlyList<SyncedSong> songs = sut.ConvertToSyncedSongs(envelope);

        SyncedSong first = songs[0];

        Assert.Equal("farottone", first.AuthorId);
        Assert.Equal("c3", first.GroupId);
        Assert.Equal("rb3xbox", first.GameFormat);
        Assert.Equal("5173c1db265c0", first.FileId);
        Assert.Equal("https://rhythmverse.co/download/5173c1db265c0", first.DownloadUrl);
    }

    [Fact]
    public async Task FetchAndConvert_WithTestFixture_SongJsonRoundTripsSongId()
    {
        string json = await File.ReadAllTextAsync("test.json");
        RhythmVerseUpstreamClient sut = BuildClient(json);

        RhythmVersePageEnvelope envelope = await sut.FetchSongsPageAsync(1, 25, null, CancellationToken.None);
        IReadOnlyList<SyncedSong> songs = sut.ConvertToSyncedSongs(envelope);

        SyncedSong first = songs[0];
        var node = System.Text.Json.Nodes.JsonNode.Parse(first.SongJson);

        Assert.NotNull(node);
        Assert.Equal(240L, (long?)node["data"]?["song_id"]);
    }

    [Fact]
    public async Task FetchSongsPage_PaginationFieldsAreParsedCorrectly()
    {
        string json = await File.ReadAllTextAsync("test.json");
        RhythmVerseUpstreamClient sut = BuildClient(json);

        RhythmVersePageEnvelope envelope = await sut.FetchSongsPageAsync(1, 25, null, CancellationToken.None);

        Assert.Equal(25, envelope.Returned);
        Assert.Equal(1, envelope.Page);
        Assert.Equal(25, envelope.Records);
        Assert.Equal(0, envelope.Start);
        Assert.True(envelope.TotalAvailable > 0);
    }

    [Fact]
    public async Task FetchSongsPage_SendsPostRequestWithCorrectMultipartFields()
    {
        string json = await File.ReadAllTextAsync("test.json");
        HttpRequestMessage? capturedRequest = null;

        HttpClient httpClient = new(new CaptureRequestHandler(
            req =>
            {
                capturedRequest = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            }))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        RhythmVerseSourceOptions sourceOptions = new()
        {
            BaseUrl = "https://rhythmverse.co",
            SongsPath = "api/all/songfiles/list",
        };

        RhythmVerseUpstreamClient sut = new(httpClient, Microsoft.Extensions.Options.Options.Create(sourceOptions), NullLogger<RhythmVerseUpstreamClient>.Instance);
        await sut.FetchSongsPageAsync(1, 25, null, CancellationToken.None);

        // Verify request contract
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.EndsWith("api/all/songfiles/list", capturedRequest.RequestUri?.ToString() ?? "");

        // Verify multipart form fields are present
        Assert.NotNull(capturedRequest.Content);
        Assert.IsType<MultipartFormDataContent>(capturedRequest.Content);
    }

    [Fact]
    public void ConvertToSyncedSongs_WithMissingRecordId_ProducesNullRecordId()
    {
        var envelope = new RhythmVersePageEnvelope
        {
            TotalAvailable = 1,
            TotalFiltered = 1,
            Returned = 1,
            Start = 0,
            Records = 25,
            Page = 1,
            Songs = [JsonNode.Parse("""{"data":{"song_id":999},"file":{"file_id":"abc"}}""")],
        };

        RhythmVerseUpstreamClient sut = BuildClient("{}");
        IReadOnlyList<SyncedSong> songs = sut.ConvertToSyncedSongs(envelope);

        Assert.Single(songs);
        Assert.Null(songs[0].RecordId);
    }

    [Fact]
    public void ConvertToSyncedSongs_WithBooleanSongNode_SkipsMalformedAndConvertsValidEntry()
    {
        RhythmVersePageEnvelope envelope = new()
        {
            TotalAvailable = 2,
            TotalFiltered = 2,
            Returned = 2,
            Start = 0,
            Records = 25,
            Page = 1,
            Songs =
            [
                JsonNode.Parse("false"),
                JsonNode.Parse("""{"data":{"song_id":321,"artist":"A"},"file":{"file_id":"f-321"}}"""),
            ],
        };

        RhythmVerseUpstreamClient sut = BuildClient("{}");
        IReadOnlyList<SyncedSong> songs = sut.ConvertToSyncedSongs(envelope);

        Assert.Single(songs);
        Assert.Equal(321L, songs[0].SongId);
    }

    [Fact]
    public void ConvertToSyncedSongs_WithBooleanDataOrFile_SkipsMalformedAndConvertsValidEntry()
    {
        RhythmVersePageEnvelope envelope = new()
        {
            TotalAvailable = 3,
            TotalFiltered = 3,
            Returned = 3,
            Start = 0,
            Records = 25,
            Page = 1,
            Songs =
            [
                JsonNode.Parse("""{"data":false,"file":{"file_id":"f-1"}}"""),
                JsonNode.Parse("""{"data":{"song_id":2},"file":false}"""),
                JsonNode.Parse("""{"data":{"song_id":654,"artist":"B"},"file":{"file_id":"f-654"}}"""),
            ],
        };

        RhythmVerseUpstreamClient sut = BuildClient("{}");
        IReadOnlyList<SyncedSong> songs = sut.ConvertToSyncedSongs(envelope);

        // data=false → still skipped (data is required to be an object)
        // file=false → now produces a partial record (no file data, but song is archived)
        // third entry → full record
        Assert.Equal(2, songs.Count);
        Assert.Contains(songs, s => s.SongId == 2L && s.FileId == string.Empty && s.FileJson == "{}");
        Assert.Contains(songs, s => s.SongId == 654L && s.FileId == "f-654");
    }

    [Fact]
    public void ConvertToSyncedSongs_WithMissingFileObject_ReturnsPartialRecord()
    {
        var envelope = new RhythmVersePageEnvelope
        {
            TotalAvailable = 1,
            TotalFiltered = 1,
            Returned = 1,
            Start = 0,
            Records = 25,
            Page = 1,
            Songs = [JsonNode.Parse("""{"data":{"song_id":777,"artist":"Test Artist","title":"Test Title"}}""")],
        };

        RhythmVerseUpstreamClient sut = BuildClient("{}");
        IReadOnlyList<SyncedSong> songs = sut.ConvertToSyncedSongs(envelope);

        Assert.Single(songs);
        SyncedSong song = songs[0];
        Assert.Equal(777L, song.SongId);
        Assert.Equal("Test Artist", song.Artist);
        Assert.Equal("Test Title", song.Title);
        Assert.Equal(string.Empty, song.FileId);
        Assert.Equal(string.Empty, song.DownloadUrl);
        Assert.Equal(string.Empty, song.AuthorId);
        Assert.Equal(string.Empty, song.GroupId);
        Assert.Equal(string.Empty, song.GameFormat);
        Assert.Equal("{}", song.FileJson);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FetchSongsPage_NonSuccessStatusCode_ThrowsHttpRequestException(HttpStatusCode statusCode)
    {
        HttpClient httpClient = new(new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(statusCode))))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        RhythmVerseSourceOptions sourceOptions = new()
        {
            BaseUrl = "https://rhythmverse.co",
            SongsPath = "api/all/songfiles/list",
        };

        RhythmVerseUpstreamClient sut = new(httpClient, Microsoft.Extensions.Options.Options.Create(sourceOptions), NullLogger<RhythmVerseUpstreamClient>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => sut.FetchSongsPageAsync(1, 25, null, CancellationToken.None));
    }

    [Fact]
    public async Task FetchSongsPage_WhenTokenConfigured_SendsBearerAuthorizationHeader()
    {
        string json = await File.ReadAllTextAsync("test.json");
        HttpRequestMessage? capturedRequest = null;

        HttpClient httpClient = new(new CaptureRequestHandler(
            req =>
            {
                capturedRequest = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            }))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        RhythmVerseSourceOptions sourceOptions = new()
        {
            BaseUrl = "https://rhythmverse.co",
            SongsPath = "api/all/songfiles/list",
            Token = "my-secret-token",
        };

        RhythmVerseUpstreamClient sut = new(httpClient, Microsoft.Extensions.Options.Options.Create(sourceOptions), NullLogger<RhythmVerseUpstreamClient>.Instance);
        await sut.FetchSongsPageAsync(1, 25, null, CancellationToken.None);

        Assert.NotNull(capturedRequest?.Headers.Authorization);
        Assert.Equal("Bearer", capturedRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("my-secret-token", capturedRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task FetchSongsPage_WhenTokenEmpty_DoesNotSendAuthorizationHeader()
    {
        string json = await File.ReadAllTextAsync("test.json");
        HttpRequestMessage? capturedRequest = null;

        HttpClient httpClient = new(new CaptureRequestHandler(
            req =>
            {
                capturedRequest = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            }))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        RhythmVerseSourceOptions sourceOptions = new()
        {
            BaseUrl = "https://rhythmverse.co",
            SongsPath = "api/all/songfiles/list",
            Token = string.Empty,
        };

        RhythmVerseUpstreamClient sut = new(httpClient, Microsoft.Extensions.Options.Options.Create(sourceOptions), NullLogger<RhythmVerseUpstreamClient>.Instance);
        await sut.FetchSongsPageAsync(1, 25, null, CancellationToken.None);

        Assert.Null(capturedRequest?.Headers.Authorization);
    }

    [Theory]
    [InlineData(0, 1)]       // page below 1 → clamped to 1
    [InlineData(-5, 1)]      // negative page → clamped to 1
    [InlineData(3, 3)]       // positive page → unchanged
    public async Task FetchSongsPage_PageBelowOne_IsClampedToOne(int inputPage, int expectedPage)
    {
        string json = await File.ReadAllTextAsync("test.json");
        HttpRequestMessage? capturedRequest = null;
        string capturedBody = string.Empty;

        HttpClient httpClient = new(new CaptureRequestHandler(
            async req =>
            {
                capturedRequest = req;
                capturedBody = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        RhythmVerseSourceOptions sourceOptions = new()
        {
            BaseUrl = "https://rhythmverse.co",
            SongsPath = "api/all/songfiles/list",
        };

        RhythmVerseUpstreamClient sut = new(httpClient, Microsoft.Extensions.Options.Options.Create(sourceOptions), NullLogger<RhythmVerseUpstreamClient>.Instance);
        await sut.FetchSongsPageAsync(inputPage, 25, null, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Contains(expectedPage.ToString(System.Globalization.CultureInfo.InvariantCulture), capturedBody);
    }

    [Theory]
    [InlineData(0, 1)]         // zero → clamped to 1
    [InlineData(251, 250)]     // over max → clamped to 250
    [InlineData(100, 100)]     // in range → unchanged
    public async Task FetchSongsPage_RecordsOutOfRange_IsClamped(int inputRecords, int expectedRecords)
    {
        string json = await File.ReadAllTextAsync("test.json");
        string capturedBody = string.Empty;

        HttpClient httpClient = new(new CaptureRequestHandler(
            async req =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        RhythmVerseSourceOptions sourceOptions = new()
        {
            BaseUrl = "https://rhythmverse.co",
            SongsPath = "api/all/songfiles/list",
        };

        RhythmVerseUpstreamClient sut = new(httpClient, Microsoft.Extensions.Options.Options.Create(sourceOptions), NullLogger<RhythmVerseUpstreamClient>.Instance);
        await sut.FetchSongsPageAsync(1, inputRecords, null, CancellationToken.None);

        Assert.Contains(expectedRecords.ToString(System.Globalization.CultureInfo.InvariantCulture), capturedBody);
    }

    [Fact]
    public void ConvertToSyncedSongs_WithPartialDownloadUrl_ResolvesAgainstBaseUrl()
    {
        RhythmVerseUpstreamClient sut = BuildClientWithBaseUrl("https://rhythmverse.co");

        RhythmVersePageEnvelope envelope = new()
        {
            TotalAvailable = 1,
            TotalFiltered = 1,
            Returned = 1,
            Start = 0,
            Records = 25,
            Page = 1,
            Songs = [JsonNode.Parse("""{"data":{"song_id":42},"file":{"file_id":"abc","download_page_url":"/download/abc"}}""")],
        };

        IReadOnlyList<SyncedSong> songs = sut.ConvertToSyncedSongs(envelope);

        Assert.Single(songs);
        Assert.Equal("https://rhythmverse.co/download/abc", songs[0].DownloadUrl);
    }

    [Fact]
    public void ConvertToSyncedSongs_WithFullDownloadUrl_PrefersFullUrl()
    {
        RhythmVerseUpstreamClient sut = BuildClientWithBaseUrl("https://rhythmverse.co");

        RhythmVersePageEnvelope envelope = new()
        {
            TotalAvailable = 1,
            TotalFiltered = 1,
            Returned = 1,
            Start = 0,
            Records = 25,
            Page = 1,
            Songs = [JsonNode.Parse("""{"data":{"song_id":43},"file":{"file_id":"def","download_page_url":"/download/def","download_page_url_full":"https://cdn.rhythmverse.co/files/def"}}""")],
        };

        IReadOnlyList<SyncedSong> songs = sut.ConvertToSyncedSongs(envelope);

        Assert.Single(songs);
        Assert.Equal("https://cdn.rhythmverse.co/files/def", songs[0].DownloadUrl);
    }

    [Fact]
    public void ConvertToSyncedSongs_WithStringNumericFields_ParsesThemCorrectly()
    {
        RhythmVerseUpstreamClient sut = BuildClient("{}");

        RhythmVersePageEnvelope envelope = new()
        {
            TotalAvailable = 1,
            TotalFiltered = 1,
            Returned = 1,
            Start = 0,
            Records = 25,
            Page = 1,
            Songs = [JsonNode.Parse("""{"data":{"song_id":"888","artist":"A","diff_guitar":"3","diff_bass":"5","diff_drums":"7","year":"2001"}}""")],
        };

        IReadOnlyList<SyncedSong> songs = sut.ConvertToSyncedSongs(envelope);

        Assert.Single(songs);
        Assert.Equal(888L, songs[0].SongId);
        Assert.Equal(3, songs[0].DiffGuitar);
        Assert.Equal(5, songs[0].DiffBass);
        Assert.Equal(7, songs[0].DiffDrums);
        Assert.Equal(2001, songs[0].Year);
    }

    [Fact]
    public void ConvertToSyncedSongs_WithNullSongNode_SkipsEntry()
    {
        RhythmVerseUpstreamClient sut = BuildClient("{}");

        RhythmVersePageEnvelope envelope = new()
        {
            TotalAvailable = 2,
            TotalFiltered = 2,
            Returned = 2,
            Start = 0,
            Records = 25,
            Page = 1,
            Songs = [null, JsonNode.Parse("""{"data":{"song_id":99},"file":{"file_id":"f-99"}}""")],
        };

        IReadOnlyList<SyncedSong> songs = sut.ConvertToSyncedSongs(envelope);

        Assert.Single(songs);
        Assert.Equal(99L, songs[0].SongId);
    }

    [Fact]
    public void ConvertToSyncedSongs_WithEmptySongsList_ReturnsEmptyList()
    {
        RhythmVerseUpstreamClient sut = BuildClient("{}");

        RhythmVersePageEnvelope envelope = new()
        {
            TotalAvailable = 0,
            TotalFiltered = 0,
            Returned = 0,
            Start = 0,
            Records = 25,
            Page = 1,
            Songs = [],
        };

        IReadOnlyList<SyncedSong> songs = sut.ConvertToSyncedSongs(envelope);

        Assert.Empty(songs);
    }

    private static RhythmVerseUpstreamClient BuildClientWithBaseUrl(string baseUrl)
    {
        HttpClient httpClient = new(new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            })))
        {
            BaseAddress = new Uri(baseUrl),
        };

        RhythmVerseSourceOptions sourceOptions = new()
        {
            BaseUrl = baseUrl,
            SongsPath = "api/all/songfiles/list",
        };

        return new RhythmVerseUpstreamClient(httpClient, Microsoft.Extensions.Options.Options.Create(sourceOptions), NullLogger<RhythmVerseUpstreamClient>.Instance);
    }

    private static RhythmVerseUpstreamClient BuildClient(string responseJson)
    {
        HttpClient httpClient = new(new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            })))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        RhythmVerseSourceOptions sourceOptions = new()
        {
            BaseUrl = "https://rhythmverse.co",
            SongsPath = "api/all/songfiles/list",
        };

        return new RhythmVerseUpstreamClient(httpClient, Microsoft.Extensions.Options.Options.Create(sourceOptions), NullLogger<RhythmVerseUpstreamClient>.Instance);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task FetchSongsPage_NonSuccessStatusCode_LogsStatusCodeWithoutResponseBody(HttpStatusCode statusCode)
    {
        const string sensitiveBody = "Bearer token=secret123 this should not appear in logs";
        CapturingLogger<RhythmVerseUpstreamClient> capturingLogger = new();

        HttpClient httpClient = new(new StubHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(sensitiveBody, Encoding.UTF8, "text/plain"),
            })))
        {
            BaseAddress = new Uri("https://rhythmverse.co"),
        };

        RhythmVerseSourceOptions sourceOptions = new()
        {
            BaseUrl = "https://rhythmverse.co",
            SongsPath = "api/all/songfiles/list",
        };

        RhythmVerseUpstreamClient sut = new(httpClient, Microsoft.Extensions.Options.Options.Create(sourceOptions), capturingLogger);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => sut.FetchSongsPageAsync(1, 25, null, CancellationToken.None));

        LogEntry entry = Assert.Single(capturingLogger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.DoesNotContain(sensitiveBody, entry.Message, StringComparison.Ordinal);
        Assert.Contains(((int)statusCode).ToString(System.Globalization.CultureInfo.InvariantCulture), entry.Message, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => sendAsync(request, cancellationToken);
    }

    private sealed class CaptureRequestHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => sendAsync(request);
    }
}
