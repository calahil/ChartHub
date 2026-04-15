using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

using ChartHub.BackupApi.Models;
using ChartHub.BackupApi.Options;
using ChartHub.BackupApi.Services;
using ChartHub.BackupApi.Tests.TestInfrastructure;

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

        RhythmVerseUpstreamClient sut = new(httpClient, Microsoft.Extensions.Options.Options.Create(sourceOptions));
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

        return new RhythmVerseUpstreamClient(httpClient, Microsoft.Extensions.Options.Options.Create(sourceOptions));
    }

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
