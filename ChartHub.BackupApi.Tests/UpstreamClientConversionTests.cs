using System.Net;
using System.Net.Http;
using System.Text;

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
        string json = await File.ReadAllTextAsync(Path.Combine("Fixtures", "test.json"));
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
        string json = await File.ReadAllTextAsync(Path.Combine("Fixtures", "test.json"));
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
        string json = await File.ReadAllTextAsync(Path.Combine("Fixtures", "test.json"));
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
        string json = await File.ReadAllTextAsync(Path.Combine("Fixtures", "test.json"));
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
        string json = await File.ReadAllTextAsync(Path.Combine("Fixtures", "test.json"));
        RhythmVerseUpstreamClient sut = BuildClient(json);

        RhythmVersePageEnvelope envelope = await sut.FetchSongsPageAsync(1, 25, null, CancellationToken.None);

        Assert.Equal(25, envelope.Returned);
        Assert.Equal(1, envelope.Page);
        Assert.Equal(25, envelope.Records);
        Assert.Equal(0, envelope.Start);
        Assert.True(envelope.TotalAvailable > 0);
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
            SongsPath = "api/songs",
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
}
