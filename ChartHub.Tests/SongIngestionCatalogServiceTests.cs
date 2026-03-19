using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class SongIngestionCatalogServiceTests
{
    [Fact]
    public void NormalizeSourceLink_StripsTrackingParameters_AndKeepsMeaningfulOnes()
    {
        var input = "https://example.com/path/file.zip?utm_source=discord&token=abc123&fbclid=zzz&ref=homepage&id=42";

        var normalized = SongIngestionCatalogService.NormalizeSourceLink(input);

        Assert.Equal("https://example.com/path/file.zip?id=42&token=abc123", normalized);
    }

    [Fact]
    public async Task GetOrCreateIngestionAsync_DeduplicatesByNormalizedLink()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-dedupe");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        var first = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-100",
            sourceLink: "https://example.com/song.zip?utm_source=discord&token=abc");

        var second = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-100",
            sourceLink: "https://example.com/song.zip?token=abc&gclid=123");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.NormalizedLink, second.NormalizedLink);
    }

    [Fact]
    public async Task StartAttemptAsync_AppendsIncrementingAttemptNumber()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-attempts");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        var ingestion = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.Encore,
            sourceId: "encore-200",
            sourceLink: "https://files.example.com/encore/song.sng?token=abc");

        var firstAttempt = await sut.StartAttemptAsync(ingestion.Id);
        var secondAttempt = await sut.StartAttemptAsync(ingestion.Id);

        Assert.Equal(1, firstAttempt.AttemptNumber);
        Assert.Equal(2, secondAttempt.AttemptNumber);
        Assert.Equal(ingestion.Id, secondAttempt.IngestionId);
    }

    [Fact]
    public async Task RecordStateTransitionAsync_AndManifestUpsert_DoNotThrow()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-state-manifest");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        var ingestion = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-300",
            sourceLink: "https://example.com/song3.zip?token=xyz");
        var attempt = await sut.StartAttemptAsync(ingestion.Id);

        await sut.RecordStateTransitionAsync(
            ingestionId: ingestion.Id,
            attemptId: attempt.Id,
            fromState: IngestionState.Queued,
            toState: IngestionState.Downloading,
            detailsJson: "{\"retry\":0}");

        await sut.UpsertManifestFileAsync(new SongInstalledManifestFileEntry(
            IngestionId: ingestion.Id,
            AttemptId: attempt.Id,
            InstallRoot: "/songs/test__rhythmverse",
            RelativePath: "notes.chart",
            Sha256: "abc123",
            SizeBytes: 2048,
            LastWriteUtc: DateTimeOffset.UtcNow,
            RecordedAtUtc: DateTimeOffset.UtcNow));

        await sut.UpsertAssetAsync(new SongIngestionAssetEntry(
            IngestionId: ingestion.Id,
            AttemptId: attempt.Id,
            AssetRole: IngestionAssetRole.Downloaded,
            Location: "/tmp/song3.zip",
            SizeBytes: 1024,
            ContentHash: "hash1",
            RecordedAtUtc: DateTimeOffset.UtcNow));
    }
}
