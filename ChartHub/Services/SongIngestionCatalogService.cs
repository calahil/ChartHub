using Microsoft.Data.Sqlite;
using ChartHub.Models;

namespace ChartHub.Services;

public enum IngestionState
{
    Queued,
    ResolvingSource,
    Downloading,
    Downloaded,
    Staged,
    Converting,
    Converted,
    Installing,
    Installed,
    Failed,
    Cancelled,
}

public enum IngestionAssetRole
{
    Downloaded,
    Staged,
    Converted,
    InstalledDirectory,
}

public enum DesktopState
{
    Cloud,
    Downloaded,
    Installed,
}

public sealed record SongIngestionRecord(
    long Id,
    string Source,
    string? SourceId,
    string SourceLink,
    string NormalizedLink,
    string? Artist,
    string? Title,
    string? Charter,
    string? LibrarySource,
    DesktopState DesktopState,
    IngestionState CurrentState,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SongIngestionAttemptRecord(
    long Id,
    long IngestionId,
    int AttemptNumber,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    IngestionState ResultState,
    string? ErrorSummary);

public sealed record SongIngestionAssetEntry(
    long IngestionId,
    long? AttemptId,
    IngestionAssetRole AssetRole,
    string Location,
    long? SizeBytes,
    string? ContentHash,
    DateTimeOffset RecordedAtUtc);

public sealed record SongInstalledManifestFileEntry(
    long IngestionId,
    long? AttemptId,
    string InstallRoot,
    string RelativePath,
    string Sha256,
    long SizeBytes,
    DateTimeOffset LastWriteUtc,
    DateTimeOffset RecordedAtUtc);

public sealed class SongIngestionCatalogService
{
    private const int SchemaVersion = 5;
    private const string SchemaVersionKey = "schema_version";
    private static readonly string[] TrackingKeys = ["fbclid", "gclid", "ref"];

    private readonly string _databasePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SongIngestionCatalogService(string databasePath)
    {
        _databasePath = databasePath;

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        Initialize();
    }

    public static string NormalizeSourceLink(string sourceLink)
    {
        if (string.IsNullOrWhiteSpace(sourceLink))
            return string.Empty;

        var trimmed = sourceLink.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return trimmed;

        var path = uri.AbsolutePath;
        if (path.Length > 1)
            path = path.TrimEnd('/');

        var queryItems = ParseQuery(uri.Query)
            .Where(item => !IsTrackingParam(item.Key))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .ToArray();

        var normalizedQuery = BuildQuery(queryItems);
        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Port = IsDefaultPort(uri) ? -1 : uri.Port,
            Path = path,
            Query = normalizedQuery,
            Fragment = string.Empty,
        };

        return builder.Uri.AbsoluteUri;
    }

    public async Task<SongIngestionRecord> GetOrCreateIngestionAsync(
        string source,
        string? sourceId,
        string sourceLink,
        string? artist = null,
        string? title = null,
        string? charter = null,
        CancellationToken cancellationToken = default,
        string? librarySource = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source is required.", nameof(source));
        if (string.IsNullOrWhiteSpace(sourceLink))
            throw new ArgumentException("Source link is required.", nameof(sourceLink));

        var normalizedSource = LibrarySourceNames.NormalizeTrustedSource(source);
        var normalizedLibrarySource = LibrarySourceNames.NormalizeTrustedSourceOrNull(librarySource);

        var now = DateTimeOffset.UtcNow;
        var normalizedLink = NormalizeSourceLink(sourceLink);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (var upsert = connection.CreateCommand())
            {
                upsert.CommandText = """
                    INSERT INTO song_ingestions (
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
                        updated_at_utc,
                        library_source
                    )
                    VALUES (
                        $source,
                        $sourceId,
                        $sourceLink,
                        $normalizedLink,
                        $artist,
                        $title,
                        $charter,
                        $desktopState,
                        $currentState,
                        $createdAtUtc,
                        $updatedAtUtc,
                        $librarySource
                    )
                    ON CONFLICT(normalized_link) DO UPDATE SET
                        source = excluded.source,
                        source_id = excluded.source_id,
                        source_link = excluded.source_link,
                        artist = COALESCE(excluded.artist, song_ingestions.artist),
                        title = COALESCE(excluded.title, song_ingestions.title),
                        charter = COALESCE(excluded.charter, song_ingestions.charter),
                        library_source = COALESCE(excluded.library_source, song_ingestions.library_source),
                        desktop_state = COALESCE(excluded.desktop_state, song_ingestions.desktop_state),
                        updated_at_utc = excluded.updated_at_utc;
                    """;
                upsert.Parameters.AddWithValue("$source", normalizedSource);
                upsert.Parameters.AddWithValue("$sourceId", (object?)sourceId ?? DBNull.Value);
                upsert.Parameters.AddWithValue("$sourceLink", sourceLink);
                upsert.Parameters.AddWithValue("$normalizedLink", normalizedLink);
                upsert.Parameters.AddWithValue("$artist", (object?)artist ?? DBNull.Value);
                upsert.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
                upsert.Parameters.AddWithValue("$charter", (object?)charter ?? DBNull.Value);
                upsert.Parameters.AddWithValue("$desktopState", DesktopState.Cloud.ToString());
                upsert.Parameters.AddWithValue("$currentState", IngestionState.Queued.ToString());
                upsert.Parameters.AddWithValue("$createdAtUtc", now.UtcDateTime.ToString("O"));
                upsert.Parameters.AddWithValue("$updatedAtUtc", now.UtcDateTime.ToString("O"));
                upsert.Parameters.AddWithValue("$librarySource", (object?)normalizedLibrarySource ?? DBNull.Value);

                await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var select = connection.CreateCommand();
            select.CommandText = """
                SELECT
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
                    updated_at_utc,
                    library_source
                FROM song_ingestions
                WHERE normalized_link = $normalizedLink;
                """;
            select.Parameters.AddWithValue("$normalizedLink", normalizedLink);

            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Failed to upsert or load ingestion record.");

            return new SongIngestionRecord(
                Id: reader.GetInt64(0),
                Source: reader.GetString(1),
                SourceId: reader.IsDBNull(2) ? null : reader.GetString(2),
                SourceLink: reader.GetString(3),
                NormalizedLink: reader.GetString(4),
                Artist: reader.IsDBNull(5) ? null : reader.GetString(5),
                Title: reader.IsDBNull(6) ? null : reader.GetString(6),
                Charter: reader.IsDBNull(7) ? null : reader.GetString(7),
                LibrarySource: reader.IsDBNull(12) ? null : reader.GetString(12),
                DesktopState: Enum.Parse<DesktopState>(reader.GetString(8), ignoreCase: true),
                CurrentState: Enum.Parse<IngestionState>(reader.GetString(9), ignoreCase: true),
                CreatedAtUtc: DateTimeOffset.Parse(reader.GetString(10)),
                UpdatedAtUtc: DateTimeOffset.Parse(reader.GetString(11)));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SongIngestionRecord?> GetLatestIngestionByAssetLocationAsync(
        string assetLocation,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assetLocation))
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    i.id,
                    i.source,
                    i.source_id,
                    i.source_link,
                    i.normalized_link,
                    i.artist,
                    i.title,
                    i.charter,
                    i.desktop_state,
                    i.current_state,
                    i.created_at_utc,
                    i.updated_at_utc,
                    i.library_source
                FROM song_assets a
                INNER JOIN song_ingestions i ON i.id = a.ingestion_id
                WHERE a.location = $location
                ORDER BY a.recorded_at_utc DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$location", assetLocation);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;

            return new SongIngestionRecord(
                Id: reader.GetInt64(0),
                Source: reader.GetString(1),
                SourceId: reader.IsDBNull(2) ? null : reader.GetString(2),
                SourceLink: reader.GetString(3),
                NormalizedLink: reader.GetString(4),
                Artist: reader.IsDBNull(5) ? null : reader.GetString(5),
                Title: reader.IsDBNull(6) ? null : reader.GetString(6),
                Charter: reader.IsDBNull(7) ? null : reader.GetString(7),
                DesktopState: Enum.Parse<DesktopState>(reader.GetString(8), ignoreCase: true),
                CurrentState: Enum.Parse<IngestionState>(reader.GetString(9), ignoreCase: true),
                CreatedAtUtc: DateTimeOffset.Parse(reader.GetString(10)),
                UpdatedAtUtc: DateTimeOffset.Parse(reader.GetString(11)),
                LibrarySource: reader.IsDBNull(12) ? null : reader.GetString(12));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SongIngestionRecord?> GetIngestionByIdAsync(
        long ingestionId,
        CancellationToken cancellationToken = default)
    {
        if (ingestionId <= 0)
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
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
                    updated_at_utc,
                    library_source
                FROM song_ingestions
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", ingestionId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;

            return new SongIngestionRecord(
                Id: reader.GetInt64(0),
                Source: reader.GetString(1),
                SourceId: reader.IsDBNull(2) ? null : reader.GetString(2),
                SourceLink: reader.GetString(3),
                NormalizedLink: reader.GetString(4),
                Artist: reader.IsDBNull(5) ? null : reader.GetString(5),
                Title: reader.IsDBNull(6) ? null : reader.GetString(6),
                Charter: reader.IsDBNull(7) ? null : reader.GetString(7),
                DesktopState: Enum.Parse<DesktopState>(reader.GetString(8), ignoreCase: true),
                CurrentState: Enum.Parse<IngestionState>(reader.GetString(9), ignoreCase: true),
                CreatedAtUtc: DateTimeOffset.Parse(reader.GetString(10)),
                UpdatedAtUtc: DateTimeOffset.Parse(reader.GetString(11)),
                LibrarySource: reader.IsDBNull(12) ? null : reader.GetString(12));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetLatestAssetLocationAsync(
        long ingestionId,
        IngestionAssetRole assetRole,
        CancellationToken cancellationToken = default)
    {
        if (ingestionId <= 0)
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT location
                FROM song_assets
                WHERE ingestion_id = $ingestionId
                  AND asset_role = $assetRole
                ORDER BY recorded_at_utc DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$ingestionId", ingestionId);
            command.Parameters.AddWithValue("$assetRole", assetRole.ToString());

            var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (scalar is null || scalar == DBNull.Value)
                return null;

            return Convert.ToString(scalar);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IngestionQueueItem?> GetQueueItemByIdAsync(
        long ingestionId,
        CancellationToken cancellationToken = default)
    {
        if (ingestionId <= 0)
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    i.id,
                    i.source,
                    i.source_id,
                    i.source_link,
                    i.artist,
                    i.title,
                    i.charter,
                    i.desktop_state,
                    i.current_state,
                    i.updated_at_utc,
                    (
                        SELECT a.location
                        FROM song_assets a
                        WHERE a.ingestion_id = i.id
                          AND a.asset_role = 'Downloaded'
                        ORDER BY a.recorded_at_utc DESC
                        LIMIT 1
                    ) AS downloaded_location,
                    (
                        SELECT a.location
                        FROM song_assets a
                        WHERE a.ingestion_id = i.id
                          AND a.asset_role = 'InstalledDirectory'
                        ORDER BY a.recorded_at_utc DESC
                        LIMIT 1
                                        ) AS installed_location,
                                        i.library_source
                FROM song_ingestions i
                WHERE i.id = $ingestionId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$ingestionId", ingestionId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;

            var source = reader.GetString(1);
            var sourceId = reader.IsDBNull(2) ? null : reader.GetString(2);
            var sourceLink = reader.GetString(3);
            var artist = reader.IsDBNull(4) ? null : reader.GetString(4);
            var title = reader.IsDBNull(5) ? null : reader.GetString(5);
            var charter = reader.IsDBNull(6) ? null : reader.GetString(6);
            var downloadedLocation = reader.IsDBNull(10) ? null : reader.GetString(10);
            var installedLocation = reader.IsDBNull(11) ? null : reader.GetString(11);
            var librarySource = reader.IsDBNull(12) ? null : reader.GetString(12);

            return new IngestionQueueItem
            {
                IngestionId = reader.GetInt64(0),
                Source = source,
                SourceId = sourceId,
                SourceLink = sourceLink,
                Artist = artist,
                Title = title,
                Charter = charter,
                DisplayName = BuildDisplayName(downloadedLocation, sourceId, sourceLink),
                DesktopState = Enum.Parse<DesktopState>(reader.GetString(7), ignoreCase: true),
                CurrentState = Enum.Parse<IngestionState>(reader.GetString(8), ignoreCase: true),
                UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(9)),
                DownloadedLocation = downloadedLocation,
                InstalledLocation = installedLocation,
                DesktopLibraryPath = installedLocation,
                LibrarySource = librarySource,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<IngestionQueueItem>> QueryQueueAsync(
        string? stateFilter,
        string? sourceFilter,
        string sortBy,
        bool descending,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        var normalizedStateFilter = string.IsNullOrWhiteSpace(stateFilter)
            || string.Equals(stateFilter, "All", StringComparison.OrdinalIgnoreCase)
            ? null
            : stateFilter.Trim();
        var normalizedSourceFilter = string.IsNullOrWhiteSpace(sourceFilter)
            || string.Equals(sourceFilter, "All", StringComparison.OrdinalIgnoreCase)
            ? null
            : sourceFilter.Trim().ToLowerInvariant();

        var orderBy = sortBy.Trim().ToLowerInvariant() switch
        {
            "source" => "i.source",
            "state" => "i.current_state",
            "name" => "downloaded_location",
            _ => "i.updated_at_utc",
        };
        var sortDirection = descending ? "DESC" : "ASC";

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT
                    i.id,
                    i.source,
                    i.source_id,
                    i.source_link,
                    i.artist,
                    i.title,
                    i.charter,
                    i.desktop_state,
                    i.current_state,
                    i.updated_at_utc,
                    (
                        SELECT a.location
                        FROM song_assets a
                        WHERE a.ingestion_id = i.id
                          AND a.asset_role = 'Downloaded'
                        ORDER BY a.recorded_at_utc DESC
                        LIMIT 1
                    ) AS downloaded_location,
                    (
                        SELECT a.location
                        FROM song_assets a
                        WHERE a.ingestion_id = i.id
                          AND a.asset_role = 'InstalledDirectory'
                        ORDER BY a.recorded_at_utc DESC
                        LIMIT 1
                                        ) AS installed_location,
                                        i.library_source
                FROM song_ingestions i
                WHERE ($stateFilter IS NULL OR i.current_state = $stateFilter)
                                    AND ($sourceFilter IS NULL OR i.source = $sourceFilter)
                ORDER BY {orderBy} {sortDirection}
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$stateFilter", (object?)normalizedStateFilter ?? DBNull.Value);
            command.Parameters.AddWithValue("$sourceFilter", (object?)normalizedSourceFilter ?? DBNull.Value);
            command.Parameters.AddWithValue("$limit", limit);

            var items = new List<IngestionQueueItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var source = reader.GetString(1);
                var sourceId = reader.IsDBNull(2) ? null : reader.GetString(2);
                var sourceLink = reader.GetString(3);
                var artist = reader.IsDBNull(4) ? null : reader.GetString(4);
                var title = reader.IsDBNull(5) ? null : reader.GetString(5);
                var charter = reader.IsDBNull(6) ? null : reader.GetString(6);
                var downloadedLocation = reader.IsDBNull(10) ? null : reader.GetString(10);
                var installedLocation = reader.IsDBNull(11) ? null : reader.GetString(11);
                var librarySource = reader.IsDBNull(12) ? null : reader.GetString(12);

                items.Add(new IngestionQueueItem
                {
                    IngestionId = reader.GetInt64(0),
                    Source = source,
                    SourceId = sourceId,
                    SourceLink = sourceLink,
                    Artist = artist,
                    Title = title,
                    Charter = charter,
                    DisplayName = BuildDisplayName(downloadedLocation, sourceId, sourceLink),
                    DesktopState = Enum.Parse<DesktopState>(reader.GetString(7), ignoreCase: true),
                    CurrentState = Enum.Parse<IngestionState>(reader.GetString(8), ignoreCase: true),
                    UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(9)),
                    DownloadedLocation = downloadedLocation,
                    InstalledLocation = installedLocation,
                    DesktopLibraryPath = installedLocation,
                    LibrarySource = librarySource,
                });
            }

            return items;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SongIngestionAttemptRecord> StartAttemptAsync(
        long ingestionId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            int nextAttemptNumber;
            await using (var getAttemptNumber = connection.CreateCommand())
            {
                getAttemptNumber.CommandText = """
                    SELECT COALESCE(MAX(attempt_number), 0) + 1
                    FROM song_attempts
                    WHERE ingestion_id = $ingestionId;
                    """;
                getAttemptNumber.Parameters.AddWithValue("$ingestionId", ingestionId);
                var scalar = await getAttemptNumber.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                nextAttemptNumber = Convert.ToInt32(scalar);
            }

            long attemptId;
            await using (var insertAttempt = connection.CreateCommand())
            {
                insertAttempt.CommandText = """
                    INSERT INTO song_attempts (
                        ingestion_id,
                        attempt_number,
                        started_at_utc,
                        ended_at_utc,
                        result_state,
                        error_summary
                    )
                    VALUES (
                        $ingestionId,
                        $attemptNumber,
                        $startedAtUtc,
                        NULL,
                        $resultState,
                        NULL
                    );
                    SELECT last_insert_rowid();
                    """;
                insertAttempt.Parameters.AddWithValue("$ingestionId", ingestionId);
                insertAttempt.Parameters.AddWithValue("$attemptNumber", nextAttemptNumber);
                insertAttempt.Parameters.AddWithValue("$startedAtUtc", now.UtcDateTime.ToString("O"));
                insertAttempt.Parameters.AddWithValue("$resultState", IngestionState.Queued.ToString());
                var scalar = await insertAttempt.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                attemptId = Convert.ToInt64(scalar);
            }

            return new SongIngestionAttemptRecord(
                Id: attemptId,
                IngestionId: ingestionId,
                AttemptNumber: nextAttemptNumber,
                StartedAtUtc: now,
                EndedAtUtc: null,
                ResultState: IngestionState.Queued,
                ErrorSummary: null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordStateTransitionAsync(
        long ingestionId,
        long? attemptId,
        IngestionState fromState,
        IngestionState toState,
        string? detailsJson,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (var eventCommand = connection.CreateCommand())
            {
                eventCommand.CommandText = """
                    INSERT INTO song_state_events (
                        ingestion_id,
                        attempt_id,
                        from_state,
                        to_state,
                        at_utc,
                        details_json
                    )
                    VALUES (
                        $ingestionId,
                        $attemptId,
                        $fromState,
                        $toState,
                        $atUtc,
                        $detailsJson
                    );
                    """;
                eventCommand.Parameters.AddWithValue("$ingestionId", ingestionId);
                eventCommand.Parameters.AddWithValue("$attemptId", (object?)attemptId ?? DBNull.Value);
                eventCommand.Parameters.AddWithValue("$fromState", fromState.ToString());
                eventCommand.Parameters.AddWithValue("$toState", toState.ToString());
                eventCommand.Parameters.AddWithValue("$atUtc", now.UtcDateTime.ToString("O"));
                eventCommand.Parameters.AddWithValue("$detailsJson", (object?)detailsJson ?? DBNull.Value);
                await eventCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var ingestionUpdate = connection.CreateCommand())
            {
                ingestionUpdate.CommandText = """
                    UPDATE song_ingestions
                    SET current_state = $state,
                        desktop_state = $desktopState,
                        updated_at_utc = $updatedAtUtc
                    WHERE id = $ingestionId;
                    """;
                ingestionUpdate.Parameters.AddWithValue("$state", toState.ToString());
                ingestionUpdate.Parameters.AddWithValue("$desktopState", MapDesktopStateFromIngestionState(toState).ToString());
                ingestionUpdate.Parameters.AddWithValue("$updatedAtUtc", now.UtcDateTime.ToString("O"));
                ingestionUpdate.Parameters.AddWithValue("$ingestionId", ingestionId);
                await ingestionUpdate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (attemptId.HasValue)
            {
                await using var attemptUpdate = connection.CreateCommand();
                attemptUpdate.CommandText = """
                    UPDATE song_attempts
                    SET result_state = $resultState,
                        ended_at_utc = CASE
                            WHEN $resultState IN ('Downloaded', 'Installed', 'Failed', 'Cancelled') THEN $endedAtUtc
                            ELSE ended_at_utc
                        END,
                        error_summary = CASE
                            WHEN $resultState = 'Failed' THEN $errorSummary
                            ELSE error_summary
                        END
                    WHERE id = $attemptId;
                    """;
                attemptUpdate.Parameters.AddWithValue("$resultState", toState.ToString());
                attemptUpdate.Parameters.AddWithValue("$endedAtUtc", now.UtcDateTime.ToString("O"));
                attemptUpdate.Parameters.AddWithValue("$errorSummary", (object?)detailsJson ?? DBNull.Value);
                attemptUpdate.Parameters.AddWithValue("$attemptId", attemptId.Value);
                await attemptUpdate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static DesktopState MapDesktopStateFromIngestionState(IngestionState state)
    {
        return state switch
        {
            IngestionState.Installed => DesktopState.Installed,
            IngestionState.Downloaded or IngestionState.Staged or IngestionState.Converting or IngestionState.Converted or IngestionState.Installing => DesktopState.Downloaded,
            _ => DesktopState.Cloud,
        };
    }

    public async Task UpsertAssetAsync(SongIngestionAssetEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.Location))
            throw new ArgumentException("Asset location is required.", nameof(entry));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO song_assets (
                    ingestion_id,
                    attempt_id,
                    asset_role,
                    location,
                    size_bytes,
                    content_hash,
                    recorded_at_utc
                )
                VALUES (
                    $ingestionId,
                    $attemptId,
                    $assetRole,
                    $location,
                    $sizeBytes,
                    $contentHash,
                    $recordedAtUtc
                )
                ON CONFLICT(ingestion_id, asset_role, location) DO UPDATE SET
                    attempt_id = excluded.attempt_id,
                    size_bytes = excluded.size_bytes,
                    content_hash = excluded.content_hash,
                    recorded_at_utc = excluded.recorded_at_utc;
                """;
            command.Parameters.AddWithValue("$ingestionId", entry.IngestionId);
            command.Parameters.AddWithValue("$attemptId", (object?)entry.AttemptId ?? DBNull.Value);
            command.Parameters.AddWithValue("$assetRole", entry.AssetRole.ToString());
            command.Parameters.AddWithValue("$location", entry.Location);
            command.Parameters.AddWithValue("$sizeBytes", (object?)entry.SizeBytes ?? DBNull.Value);
            command.Parameters.AddWithValue("$contentHash", (object?)entry.ContentHash ?? DBNull.Value);
            command.Parameters.AddWithValue("$recordedAtUtc", entry.RecordedAtUtc.UtcDateTime.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertManifestFileAsync(SongInstalledManifestFileEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.InstallRoot))
            throw new ArgumentException("Install root is required.", nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.RelativePath))
            throw new ArgumentException("Relative path is required.", nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.Sha256))
            throw new ArgumentException("SHA-256 is required.", nameof(entry));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO installed_manifest_files (
                    ingestion_id,
                    attempt_id,
                    install_root,
                    relative_path,
                    sha256,
                    size_bytes,
                    last_write_utc,
                    recorded_at_utc
                )
                VALUES (
                    $ingestionId,
                    $attemptId,
                    $installRoot,
                    $relativePath,
                    $sha256,
                    $sizeBytes,
                    $lastWriteUtc,
                    $recordedAtUtc
                )
                ON CONFLICT(ingestion_id, install_root, relative_path) DO UPDATE SET
                    attempt_id = excluded.attempt_id,
                    sha256 = excluded.sha256,
                    size_bytes = excluded.size_bytes,
                    last_write_utc = excluded.last_write_utc,
                    recorded_at_utc = excluded.recorded_at_utc;
                """;
            command.Parameters.AddWithValue("$ingestionId", entry.IngestionId);
            command.Parameters.AddWithValue("$attemptId", (object?)entry.AttemptId ?? DBNull.Value);
            command.Parameters.AddWithValue("$installRoot", entry.InstallRoot);
            command.Parameters.AddWithValue("$relativePath", entry.RelativePath);
            command.Parameters.AddWithValue("$sha256", entry.Sha256);
            command.Parameters.AddWithValue("$sizeBytes", entry.SizeBytes);
            command.Parameters.AddWithValue("$lastWriteUtc", entry.LastWriteUtc.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$recordedAtUtc", entry.RecordedAtUtc.UtcDateTime.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Initialize()
    {
        using var connection = CreateConnection();
        connection.Open();

        EnsureSchemaMetadataTable(connection);
        RebuildSchema(connection);
        EnsureColumnExists(connection, "song_ingestions", "desktop_state", "TEXT NOT NULL DEFAULT 'Cloud'");
        EnsureColumnExists(connection, "song_ingestions", "artist", "TEXT NULL");
        EnsureColumnExists(connection, "song_ingestions", "title", "TEXT NULL");
        EnsureColumnExists(connection, "song_ingestions", "charter", "TEXT NULL");
        EnsureColumnExists(connection, "song_ingestions", "library_source", "TEXT NULL");

        using (var cleanup = connection.CreateCommand())
        {
            cleanup.CommandText = """
                DELETE FROM installed_manifest_files
                WHERE ingestion_id IN (
                    SELECT id
                    FROM song_ingestions
                    WHERE lower(source) NOT IN ('rhythmverse', 'encore')
                       OR (library_source IS NOT NULL AND lower(library_source) NOT IN ('rhythmverse', 'encore'))
                );

                DELETE FROM song_assets
                WHERE ingestion_id IN (
                    SELECT id
                    FROM song_ingestions
                    WHERE lower(source) NOT IN ('rhythmverse', 'encore')
                       OR (library_source IS NOT NULL AND lower(library_source) NOT IN ('rhythmverse', 'encore'))
                );

                DELETE FROM song_state_events
                WHERE ingestion_id IN (
                    SELECT id
                    FROM song_ingestions
                    WHERE lower(source) NOT IN ('rhythmverse', 'encore')
                       OR (library_source IS NOT NULL AND lower(library_source) NOT IN ('rhythmverse', 'encore'))
                );

                DELETE FROM song_attempts
                WHERE ingestion_id IN (
                    SELECT id
                    FROM song_ingestions
                    WHERE lower(source) NOT IN ('rhythmverse', 'encore')
                       OR (library_source IS NOT NULL AND lower(library_source) NOT IN ('rhythmverse', 'encore'))
                );

                DELETE FROM song_ingestions
                WHERE lower(source) NOT IN ('rhythmverse', 'encore')
                   OR (library_source IS NOT NULL AND lower(library_source) NOT IN ('rhythmverse', 'encore'));
                """;
            cleanup.ExecuteNonQuery();
        }

        var existingVersion = GetSchemaVersion(connection);
        if (existingVersion is null || existingVersion < SchemaVersion)
            SetSchemaVersion(connection, SchemaVersion);
    }

    private static void RebuildSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS song_ingestions (
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
                library_source TEXT NULL,
                UNIQUE(normalized_link)
            );

            CREATE TABLE IF NOT EXISTS song_attempts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                ingestion_id INTEGER NOT NULL,
                attempt_number INTEGER NOT NULL,
                started_at_utc TEXT NOT NULL,
                ended_at_utc TEXT NULL,
                result_state TEXT NOT NULL,
                error_summary TEXT NULL,
                UNIQUE(ingestion_id, attempt_number),
                FOREIGN KEY(ingestion_id) REFERENCES song_ingestions(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS song_state_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                ingestion_id INTEGER NOT NULL,
                attempt_id INTEGER NULL,
                from_state TEXT NOT NULL,
                to_state TEXT NOT NULL,
                at_utc TEXT NOT NULL,
                details_json TEXT NULL,
                FOREIGN KEY(ingestion_id) REFERENCES song_ingestions(id) ON DELETE CASCADE,
                FOREIGN KEY(attempt_id) REFERENCES song_attempts(id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS song_assets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                ingestion_id INTEGER NOT NULL,
                attempt_id INTEGER NULL,
                asset_role TEXT NOT NULL,
                location TEXT NOT NULL,
                size_bytes INTEGER NULL,
                content_hash TEXT NULL,
                recorded_at_utc TEXT NOT NULL,
                UNIQUE(ingestion_id, asset_role, location),
                FOREIGN KEY(ingestion_id) REFERENCES song_ingestions(id) ON DELETE CASCADE,
                FOREIGN KEY(attempt_id) REFERENCES song_attempts(id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS installed_manifest_files (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                ingestion_id INTEGER NOT NULL,
                attempt_id INTEGER NULL,
                install_root TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                sha256 TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                last_write_utc TEXT NOT NULL,
                recorded_at_utc TEXT NOT NULL,
                UNIQUE(ingestion_id, install_root, relative_path),
                FOREIGN KEY(ingestion_id) REFERENCES song_ingestions(id) ON DELETE CASCADE,
                FOREIGN KEY(attempt_id) REFERENCES song_attempts(id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS idx_song_ingestions_state
                ON song_ingestions(current_state);

            CREATE INDEX IF NOT EXISTS idx_song_attempts_ingestion
                ON song_attempts(ingestion_id);

            CREATE INDEX IF NOT EXISTS idx_song_state_events_ingestion
                ON song_state_events(ingestion_id);

            CREATE INDEX IF NOT EXISTS idx_song_assets_ingestion
                ON song_assets(ingestion_id);

            CREATE INDEX IF NOT EXISTS idx_manifest_ingestion
                ON installed_manifest_files(ingestion_id);

            CREATE TABLE IF NOT EXISTS library_songs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                local_path TEXT NOT NULL,
                artist TEXT NULL,
                title TEXT NULL,
                charter TEXT NULL,
                internal_identity_key TEXT NULL,
                content_identity_hash TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                UNIQUE(local_path)
            );

            CREATE TABLE IF NOT EXISTS library_song_keys (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                library_song_id INTEGER NOT NULL,
                source TEXT NOT NULL,
                source_id TEXT NOT NULL,
                external_key_hash TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                UNIQUE(external_key_hash),
                UNIQUE(library_song_id, source),
                FOREIGN KEY(library_song_id) REFERENCES library_songs(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_library_songs_content_identity_hash
                ON library_songs(content_identity_hash);

            CREATE INDEX IF NOT EXISTS idx_library_song_keys_source
                ON library_song_keys(source, source_id);

            CREATE INDEX IF NOT EXISTS idx_library_song_keys_library_song_id
                ON library_song_keys(library_song_id);
            """;
        command.ExecuteNonQuery();
    }

    private static void EnsureSchemaMetadataTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static int? GetSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM schema_metadata WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", SchemaVersionKey);
        var value = command.ExecuteScalar();
        if (value is null || value is DBNull)
            return null;

        return int.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
    }

    private static void SetSchemaVersion(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO schema_metadata (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", SchemaVersionKey);
        command.Parameters.AddWithValue("$value", version.ToString());
        command.ExecuteNonQuery();
    }

    private static void EnsureColumnExists(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";

        try
        {
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            // Existing installs may already have this column.
        }
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
    }

    private static bool IsTrackingParam(string key)
    {
        if (key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
            return true;

        return TrackingKeys.Any(trackingKey => string.Equals(key, trackingKey, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDefaultPort(Uri uri)
    {
        return (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && uri.Port == 80)
            || (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && uri.Port == 443);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> ParseQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var raw = query.StartsWith('?') ? query[1..] : query;
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var list = new List<KeyValuePair<string, string>>();
        foreach (var chunk in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var splitIndex = chunk.IndexOf('=');
            var key = splitIndex < 0 ? chunk : chunk[..splitIndex];
            var value = splitIndex < 0 ? string.Empty : chunk[(splitIndex + 1)..];

            var decodedKey = Uri.UnescapeDataString(key.Replace('+', ' '));
            var decodedValue = Uri.UnescapeDataString(value.Replace('+', ' '));
            if (string.IsNullOrWhiteSpace(decodedKey))
                continue;

            list.Add(new KeyValuePair<string, string>(decodedKey, decodedValue));
        }

        return list;
    }

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> queryItems)
    {
        var encoded = queryItems
            .Select(item => string.IsNullOrWhiteSpace(item.Value)
                ? Uri.EscapeDataString(item.Key)
                : $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}")
            .ToArray();

        return encoded.Length == 0 ? string.Empty : string.Join("&", encoded);
    }

    private static string BuildDisplayName(string? downloadedLocation, string? sourceId, string sourceLink)
    {
        if (!string.IsNullOrWhiteSpace(downloadedLocation))
            return Path.GetFileName(downloadedLocation);

        if (!string.IsNullOrWhiteSpace(sourceId))
            return sourceId;

        return sourceLink;
    }
}
