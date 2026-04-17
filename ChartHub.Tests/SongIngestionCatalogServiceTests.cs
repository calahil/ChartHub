using ChartHub.Models;
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
        string input = "https://example.com/path/file.zip?utm_source=discord&token=abc123&fbclid=zzz&ref=homepage&id=42";

        string normalized = SongIngestionCatalogService.NormalizeSourceLink(input);

        Assert.Equal("https://example.com/path/file.zip?id=42&token=abc123", normalized);
    }

    [Fact]
    public async Task GetOrCreateIngestionAsync_DeduplicatesByNormalizedLink()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-dedupe");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord first = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-100",
            sourceLink: "https://example.com/song.zip?utm_source=discord&token=abc");

        SongIngestionRecord second = await sut.GetOrCreateIngestionAsync(
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

        SongIngestionRecord ingestion = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.Encore,
            sourceId: "encore-200",
            sourceLink: "https://files.example.com/encore/song.sng?token=abc");

        SongIngestionAttemptRecord firstAttempt = await sut.StartAttemptAsync(ingestion.Id);
        SongIngestionAttemptRecord secondAttempt = await sut.StartAttemptAsync(ingestion.Id);

        Assert.Equal(1, firstAttempt.AttemptNumber);
        Assert.Equal(2, secondAttempt.AttemptNumber);
        Assert.Equal(ingestion.Id, secondAttempt.IngestionId);
    }

    [Fact]
    public async Task RecordStateTransitionAsync_AndManifestUpsert_DoNotThrow()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-state-manifest");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord ingestion = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-300",
            sourceLink: "https://example.com/song3.zip?token=xyz");
        SongIngestionAttemptRecord attempt = await sut.StartAttemptAsync(ingestion.Id);

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
        string databasePath = Path.Combine(temp.RootPath, "library-catalog.db");

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();

            await using SqliteCommand command = connection.CreateCommand();
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

        SongIngestionRecord? ingestion = await sut.GetIngestionByIdAsync(1);
        Assert.NotNull(ingestion);
        Assert.Equal("Legacy Artist", ingestion!.Artist);
        Assert.Equal("Legacy Title", ingestion.Title);
        Assert.Equal("Legacy Charter", ingestion.Charter);
        Assert.Null(ingestion.LibrarySource);

        IngestionQueueItem? queueItem = await sut.GetQueueItemByIdAsync(1);
        Assert.NotNull(queueItem);
        Assert.Equal("/songs/Legacy Artist/Legacy Title/Legacy Charter__rhythmverse", queueItem!.InstalledLocation);
        Assert.Equal(queueItem.InstalledLocation, queueItem.DesktopLibraryPath);

        await using var verifyConnection = new SqliteConnection($"Data Source={databasePath}");
        await verifyConnection.OpenAsync();

        await using SqliteCommand versionCommand = verifyConnection.CreateCommand();
        versionCommand.CommandText = "SELECT value FROM schema_metadata WHERE key = 'schema_version';";
        string? version = Convert.ToString(await versionCommand.ExecuteScalarAsync());
        Assert.Equal("5", version);

        await using SqliteCommand pragmaCommand = verifyConnection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA table_info(song_ingestions);";
        await using SqliteDataReader reader = await pragmaCommand.ExecuteReaderAsync();

        bool hasLibrarySource = false;
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

    [Fact]
    public async Task GetOrCreateIngestionAsync_SetsMetadataFields_OnFirstCreate()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-metadata-create");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord record = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-meta-1",
            sourceLink: "https://example.com/meta.zip?token=abc",
            artist: "Test Artist",
            title: "Test Title",
            charter: "Test Charter");

        Assert.Equal("Test Artist", record.Artist);
        Assert.Equal("Test Title", record.Title);
        Assert.Equal("Test Charter", record.Charter);
        Assert.Equal(IngestionState.Queued, record.CurrentState);
        Assert.Equal(DesktopState.Cloud, record.DesktopState);
    }

    [Fact]
    public async Task GetOrCreateIngestionAsync_PreservesExistingMetadata_OnConflictWithNullValues()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-metadata-preserve");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-preserve-1",
            sourceLink: "https://example.com/pres.zip?token=abc",
            artist: "Original Artist",
            title: "Original Title",
            charter: "Original Charter");

        SongIngestionRecord second = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-preserve-1",
            sourceLink: "https://example.com/pres.zip?token=abc",
            artist: null,
            title: null,
            charter: null);

        Assert.Equal("Original Artist", second.Artist);
        Assert.Equal("Original Title", second.Title);
        Assert.Equal("Original Charter", second.Charter);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetOrCreateIngestionAsync_ThrowsOnBlankSource(string source)
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-blank-source");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.GetOrCreateIngestionAsync(source: source, sourceId: "id1", sourceLink: "https://example.com/a.zip?token=x"));
    }

    [Fact]
    public async Task GetIngestionByIdAsync_ReturnsNull_ForNonExistentId()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-notfound");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord? result = await sut.GetIngestionByIdAsync(99999);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetIngestionByIdAsync_ReturnsNull_ForNonPositiveId(long id)
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-nonpositive-id");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord? result = await sut.GetIngestionByIdAsync(id);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatestIngestionBySourceKeyAsync_ReturnsRecord_WhenExists()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-by-source-key");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord created = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-sourcekey-1",
            sourceLink: "https://example.com/sourcekey.zip?token=abc");

        SongIngestionRecord? found = await sut.GetLatestIngestionBySourceKeyAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-sourcekey-1");

        Assert.NotNull(found);
        Assert.Equal(created.Id, found!.Id);
    }

    [Fact]
    public async Task GetLatestIngestionBySourceKeyAsync_ReturnsNull_WhenNotExists()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-by-source-key-miss");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord? found = await sut.GetLatestIngestionBySourceKeyAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-does-not-exist");

        Assert.Null(found);
    }

    [Fact]
    public async Task GetLatestIngestionByAssetLocationAsync_ReturnsRecord_WhenAssetExists()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-by-asset-location");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord ingestion = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-asset-1",
            sourceLink: "https://example.com/asset.zip?token=abc");

        SongIngestionAttemptRecord attempt = await sut.StartAttemptAsync(ingestion.Id);

        await sut.UpsertAssetAsync(new SongIngestionAssetEntry(
            IngestionId: ingestion.Id,
            AttemptId: attempt.Id,
            AssetRole: IngestionAssetRole.Downloaded,
            Location: "/tmp/downloads/asset.zip",
            SizeBytes: 512,
            ContentHash: null,
            RecordedAtUtc: DateTimeOffset.UtcNow));

        SongIngestionRecord? found = await sut.GetLatestIngestionByAssetLocationAsync("/tmp/downloads/asset.zip");

        Assert.NotNull(found);
        Assert.Equal(ingestion.Id, found!.Id);
    }

    [Fact]
    public async Task GetLatestIngestionByAssetLocationAsync_ReturnsNull_WhenNotExists()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-by-asset-location-miss");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord? found = await sut.GetLatestIngestionByAssetLocationAsync("/no/such/path.zip");

        Assert.Null(found);
    }

    [Fact]
    public async Task GetLatestAssetLocationAsync_ReturnsLocation_WhenAssetExists()
    {
        using var temp = new TemporaryDirectoryFixture("asset-location-roundtrip");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord ingestion = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-loc-1",
            sourceLink: "https://example.com/loc.zip?token=abc");

        await sut.UpsertAssetAsync(new SongIngestionAssetEntry(
            IngestionId: ingestion.Id,
            AttemptId: null,
            AssetRole: IngestionAssetRole.InstalledDirectory,
            Location: "/songs/Test Artist/Test Title/Test Charter__rhythmverse",
            SizeBytes: null,
            ContentHash: null,
            RecordedAtUtc: DateTimeOffset.UtcNow));

        string? location = await sut.GetLatestAssetLocationAsync(ingestion.Id, IngestionAssetRole.InstalledDirectory);

        Assert.Equal("/songs/Test Artist/Test Title/Test Charter__rhythmverse", location);
    }

    [Fact]
    public async Task GetLatestAssetLocationAsync_ReturnsNull_WhenNoAssetOfRole()
    {
        using var temp = new TemporaryDirectoryFixture("asset-location-miss");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord ingestion = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-loc-miss",
            sourceLink: "https://example.com/locmiss.zip?token=abc");

        string? location = await sut.GetLatestAssetLocationAsync(ingestion.Id, IngestionAssetRole.InstalledDirectory);

        Assert.Null(location);
    }

    [Fact]
    public async Task RemoveIngestionAsync_DeletesRecordAndCascades()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-remove-cascade");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord ingestion = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-remove-1",
            sourceLink: "https://example.com/remove.zip?token=abc");

        SongIngestionAttemptRecord attempt = await sut.StartAttemptAsync(ingestion.Id);

        await sut.UpsertAssetAsync(new SongIngestionAssetEntry(
            IngestionId: ingestion.Id,
            AttemptId: attempt.Id,
            AssetRole: IngestionAssetRole.Downloaded,
            Location: "/tmp/remove.zip",
            SizeBytes: null,
            ContentHash: null,
            RecordedAtUtc: DateTimeOffset.UtcNow));

        await sut.RemoveIngestionAsync(ingestion.Id);

        SongIngestionRecord? afterDelete = await sut.GetIngestionByIdAsync(ingestion.Id);
        Assert.Null(afterDelete);

        // Asset should also be deleted via cascade
        string? assetLocation = await sut.GetLatestAssetLocationAsync(ingestion.Id, IngestionAssetRole.Downloaded);
        Assert.Null(assetLocation);
    }

    [Fact]
    public async Task RecordStateTransitionAsync_UpdatesDesktopState_ToDownloadedWhenDownloading()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-desktop-state-transition");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord ingestion = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-transition-1",
            sourceLink: "https://example.com/trans.zip?token=abc");

        Assert.Equal(DesktopState.Cloud, ingestion.DesktopState);

        await sut.RecordStateTransitionAsync(
            ingestionId: ingestion.Id,
            attemptId: null,
            fromState: IngestionState.Queued,
            toState: IngestionState.Downloaded,
            detailsJson: null);

        SongIngestionRecord? updated = await sut.GetIngestionByIdAsync(ingestion.Id);
        Assert.NotNull(updated);
        Assert.Equal(IngestionState.Downloaded, updated!.CurrentState);
        Assert.Equal(DesktopState.Downloaded, updated.DesktopState);
    }

    [Fact]
    public async Task RecordStateTransitionAsync_UpdatesDesktopState_ToInstalledWhenInstalled()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-desktop-state-installed");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord ingestion = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-transition-installed",
            sourceLink: "https://example.com/transi.zip?token=abc");

        await sut.RecordStateTransitionAsync(
            ingestionId: ingestion.Id,
            attemptId: null,
            fromState: IngestionState.Installing,
            toState: IngestionState.Installed,
            detailsJson: null);

        SongIngestionRecord? updated = await sut.GetIngestionByIdAsync(ingestion.Id);
        Assert.NotNull(updated);
        Assert.Equal(DesktopState.Installed, updated!.DesktopState);
    }

    [Fact]
    public async Task RecordStateTransitionAsync_UpdatesDesktopState_BackToCloudWhenFailed()
    {
        using var temp = new TemporaryDirectoryFixture("ingestion-desktop-state-failed");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord ingestion = await sut.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-transition-failed",
            sourceLink: "https://example.com/transf.zip?token=abc");

        await sut.RecordStateTransitionAsync(
            ingestionId: ingestion.Id,
            attemptId: null,
            fromState: IngestionState.Downloading,
            toState: IngestionState.Failed,
            detailsJson: "{\"error\":\"timeout\"}");

        SongIngestionRecord? updated = await sut.GetIngestionByIdAsync(ingestion.Id);
        Assert.NotNull(updated);
        Assert.Equal(IngestionState.Failed, updated!.CurrentState);
        Assert.Equal(DesktopState.Cloud, updated.DesktopState);
    }

    [Fact]
    public async Task QueryQueueAsync_ReturnsAll_WhenNoFilters()
    {
        using var temp = new TemporaryDirectoryFixture("queue-no-filters");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        await sut.GetOrCreateIngestionAsync(LibrarySourceNames.RhythmVerse, "rv-q1", "https://example.com/q1.zip?token=a");
        await sut.GetOrCreateIngestionAsync(LibrarySourceNames.Encore, "e-q2", "https://example.com/q2.zip?token=b");

        IReadOnlyList<IngestionQueueItem> items = await sut.QueryQueueAsync(
            stateFilter: null,
            sourceFilter: null,
            sortBy: "date",
            descending: true);

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task QueryQueueAsync_FiltersBy_State()
    {
        using var temp = new TemporaryDirectoryFixture("queue-state-filter");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        SongIngestionRecord ingestion = await sut.GetOrCreateIngestionAsync(
            LibrarySourceNames.RhythmVerse, "rv-qsf1", "https://example.com/qsf1.zip?token=a");

        await sut.GetOrCreateIngestionAsync(
            LibrarySourceNames.RhythmVerse, "rv-qsf2", "https://example.com/qsf2.zip?token=b");

        await sut.RecordStateTransitionAsync(
            ingestionId: ingestion.Id,
            attemptId: null,
            fromState: IngestionState.Queued,
            toState: IngestionState.Downloaded,
            detailsJson: null);

        IReadOnlyList<IngestionQueueItem> onlyDownloaded = await sut.QueryQueueAsync(
            stateFilter: "Downloaded",
            sourceFilter: null,
            sortBy: "date",
            descending: false);

        Assert.Single(onlyDownloaded);
        Assert.Equal(ingestion.Id, onlyDownloaded[0].IngestionId);
    }

    [Fact]
    public async Task QueryQueueAsync_FiltersBy_Source()
    {
        using var temp = new TemporaryDirectoryFixture("queue-source-filter");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        await sut.GetOrCreateIngestionAsync(LibrarySourceNames.RhythmVerse, "rv-qsrc1", "https://example.com/qsrc1.zip?token=a");
        await sut.GetOrCreateIngestionAsync(LibrarySourceNames.Encore, "e-qsrc2", "https://example.com/qsrc2.zip?token=b");

        IReadOnlyList<IngestionQueueItem> rvOnly = await sut.QueryQueueAsync(
            stateFilter: null,
            sourceFilter: LibrarySourceNames.RhythmVerse,
            sortBy: "date",
            descending: false);

        Assert.Single(rvOnly);
        Assert.Equal(LibrarySourceNames.RhythmVerse, rvOnly[0].Source);
    }

    [Fact]
    public async Task QueryQueueAsync_RespectsLimit()
    {
        using var temp = new TemporaryDirectoryFixture("queue-limit");
        var sut = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        for (int i = 0; i < 5; i++)
        {
            await sut.GetOrCreateIngestionAsync(LibrarySourceNames.RhythmVerse, $"rv-ql{i}", $"https://example.com/ql{i}.zip?token={i}");
        }

        IReadOnlyList<IngestionQueueItem> limited = await sut.QueryQueueAsync(
            stateFilter: null,
            sourceFilter: null,
            sortBy: "date",
            descending: false,
            limit: 3);

        Assert.Equal(3, limited.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void NormalizeSourceLink_ReturnsEmpty_ForBlankInput(string input)
    {
        string result = SongIngestionCatalogService.NormalizeSourceLink(input);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void NormalizeSourceLink_ReturnsInput_ForNonUrl()
    {
        const string nonUrl = "not-a-url";

        string result = SongIngestionCatalogService.NormalizeSourceLink(nonUrl);

        Assert.Equal(nonUrl, result);
    }

    [Fact]
    public void NormalizeSourceLink_StripsFragment()
    {
        string result = SongIngestionCatalogService.NormalizeSourceLink("https://example.com/song.zip?token=abc#section");

        Assert.Equal("https://example.com/song.zip?token=abc", result);
    }

    [Fact]
    public void NormalizeSourceLink_StripsTrailingSlash_FromPath()
    {
        string result = SongIngestionCatalogService.NormalizeSourceLink("https://example.com/path/");

        Assert.DoesNotContain("/path/", result);
    }

    [Theory]
    [InlineData("http://example.com:80/a.zip?token=abc", "http://example.com/a.zip?token=abc")]
    [InlineData("https://example.com:443/a.zip?token=abc", "https://example.com/a.zip?token=abc")]
    public void NormalizeSourceLink_StripsDefaultPort(string input, string expected)
    {
        string result = SongIngestionCatalogService.NormalizeSourceLink(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeSourceLink_NormalizesSchemeAndHostToLowercase()
    {
        string result = SongIngestionCatalogService.NormalizeSourceLink("HTTPS://Example.COM/song.zip?token=abc");

        Assert.StartsWith("https://example.com/", result);
    }

    [Fact]
    public void EnsureColumnExists_ThrowsArgumentException_ForInvalidTableName()
    {
        using var temp = new TemporaryDirectoryFixture("ensure-column-invalid-table");
        string dbPath = Path.Combine(temp.RootPath, "test.db");

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        // Use reflection to make the private static method accessible
        System.Reflection.MethodInfo? method = typeof(SongIngestionCatalogService).GetMethod(
            "EnsureColumnExists",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        System.Reflection.TargetInvocationException ex = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => method!.Invoke(null, [connection, "song_ingestions; DROP TABLE--", "some_col", "TEXT NULL"]));

        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    [Fact]
    public void EnsureColumnExists_ThrowsArgumentException_ForInvalidColumnName()
    {
        using var temp = new TemporaryDirectoryFixture("ensure-column-invalid-col");
        string dbPath = Path.Combine(temp.RootPath, "test.db");

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        System.Reflection.MethodInfo? method = typeof(SongIngestionCatalogService).GetMethod(
            "EnsureColumnExists",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        System.Reflection.TargetInvocationException ex = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => method!.Invoke(null, [connection, "song_ingestions", "col'; DROP TABLE song_ingestions;--", "TEXT NULL"]));

        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    [Fact]
    public void EnsureColumnExists_ThrowsArgumentException_ForInvalidColumnDefinition()
    {
        using var temp = new TemporaryDirectoryFixture("ensure-column-invalid-def");
        string dbPath = Path.Combine(temp.RootPath, "test.db");

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        System.Reflection.MethodInfo? method = typeof(SongIngestionCatalogService).GetMethod(
            "EnsureColumnExists",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        System.Reflection.TargetInvocationException ex = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => method!.Invoke(null, [connection, "song_ingestions", "some_col", "TEXT; DROP TABLE song_ingestions;--"]));

        Assert.IsType<ArgumentException>(ex.InnerException);
    }
}
