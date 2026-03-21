using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;

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

    [Fact]
    public async Task Initialize_WhenUpgradingExistingSchema_PreservesExistingIngestionData()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-migration-preserve");
        var databasePath = Path.Combine(temp.RootPath, "library-catalog.db");

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE schema_metadata (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                INSERT INTO schema_metadata (key, value)
                VALUES ('schema_version', '4');

                CREATE TABLE song_ingestions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    source TEXT NOT NULL,
                    source_id TEXT NULL,
                    source_link TEXT NOT NULL,
                    normalized_link TEXT NOT NULL,
                    artist TEXT NULL,
                    title TEXT NULL,
                    charter TEXT NULL,
                    desktop_state TEXT NOT NULL,
                    current_state TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    UNIQUE(normalized_link)
                );

                CREATE TABLE song_attempts (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ingestion_id INTEGER NOT NULL,
                    attempt_number INTEGER NOT NULL,
                    started_at_utc TEXT NOT NULL,
                    ended_at_utc TEXT NULL,
                    result_state TEXT NOT NULL,
                    error_summary TEXT NULL,
                    UNIQUE(ingestion_id, attempt_number)
                );

                CREATE TABLE song_state_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ingestion_id INTEGER NOT NULL,
                    attempt_id INTEGER NULL,
                    from_state TEXT NOT NULL,
                    to_state TEXT NOT NULL,
                    at_utc TEXT NOT NULL,
                    details_json TEXT NULL
                );

                CREATE TABLE song_assets (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ingestion_id INTEGER NOT NULL,
                    attempt_id INTEGER NULL,
                    asset_role TEXT NOT NULL,
                    location TEXT NOT NULL,
                    size_bytes INTEGER NULL,
                    content_hash TEXT NULL,
                    recorded_at_utc TEXT NOT NULL,
                    UNIQUE(ingestion_id, asset_role, location)
                );

                CREATE TABLE installed_manifest_files (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ingestion_id INTEGER NOT NULL,
                    attempt_id INTEGER NULL,
                    install_root TEXT NOT NULL,
                    relative_path TEXT NOT NULL,
                    sha256 TEXT NOT NULL,
                    size_bytes INTEGER NOT NULL,
                    last_write_utc TEXT NOT NULL,
                    recorded_at_utc TEXT NOT NULL,
                    UNIQUE(ingestion_id, install_root, relative_path)
                );

                INSERT INTO song_ingestions (
                    id,
                    source,
                    source_id,
                    source_link,
                    normalized_link,
                    artist,
                    title,
                    charter,
                    desktop_state,
                    current_state,
                    created_at_utc,
                    updated_at_utc
                ) VALUES (
                    1,
                    'rhythmverse',
                    'rv-legacy',
                    'https://example.com/song.zip?token=abc',
                    'https://example.com/song.zip?token=abc',
                    'Legacy Artist',
                    'Legacy Title',
                    'Legacy Charter',
                    'Installed',
                    'Installed',
                    '2026-03-18T12:00:00.0000000Z',
                    '2026-03-18T12:30:00.0000000Z'
                );

                INSERT INTO song_assets (
                    ingestion_id,
                    attempt_id,
                    asset_role,
                    location,
                    size_bytes,
                    content_hash,
                    recorded_at_utc
                ) VALUES (
                    1,
                    NULL,
                    'InstalledDirectory',
                    '/songs/Legacy Artist/Legacy Title/Legacy Charter__rhythmverse',
                    NULL,
                    NULL,
                    '2026-03-18T12:31:00.0000000Z'
                );
                """;

            await command.ExecuteNonQueryAsync();
        }

        var sut = new SongIngestionCatalogService(databasePath);

        var ingestion = await sut.GetIngestionByIdAsync(1);
        Assert.NotNull(ingestion);
        Assert.Equal("Legacy Artist", ingestion!.Artist);
        Assert.Equal("Legacy Title", ingestion.Title);
        Assert.Equal("Legacy Charter", ingestion.Charter);
        Assert.Null(ingestion.LibrarySource);

        var queueItem = await sut.GetQueueItemByIdAsync(1);
        Assert.NotNull(queueItem);
        Assert.Equal("/songs/Legacy Artist/Legacy Title/Legacy Charter__rhythmverse", queueItem!.InstalledLocation);
        Assert.Equal(queueItem.InstalledLocation, queueItem.DesktopLibraryPath);

        await using var verifyConnection = new SqliteConnection($"Data Source={databasePath}");
        await verifyConnection.OpenAsync();

        await using var versionCommand = verifyConnection.CreateCommand();
        versionCommand.CommandText = "SELECT value FROM schema_metadata WHERE key = 'schema_version';";
        var version = Convert.ToString(await versionCommand.ExecuteScalarAsync());
        Assert.Equal("5", version);

        await using var pragmaCommand = verifyConnection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA table_info(song_ingestions);";
        await using var reader = await pragmaCommand.ExecuteReaderAsync();

        var hasLibrarySource = false;
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), "library_source", StringComparison.OrdinalIgnoreCase))
            {
                hasLibrarySource = true;
                break;
            }
        }

        Assert.True(hasLibrarySource);
    }
}
