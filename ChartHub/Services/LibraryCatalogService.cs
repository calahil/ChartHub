using Microsoft.Data.Sqlite;

namespace ChartHub.Services;

public static class LibrarySourceNames
{
    public const string RhythmVerse = "rhythmverse";
    public const string Encore = "encore";

    public static bool IsTrustedSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        var normalized = source.Trim().ToLowerInvariant();
        return normalized is RhythmVerse or Encore;
    }

    public static string NormalizeTrustedSource(string? source, string paramName = "source")
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Only rhythmverse and encore sources are supported.", paramName);

        var normalized = source.Trim().ToLowerInvariant();
        return normalized switch
        {
            RhythmVerse => RhythmVerse,
            Encore => Encore,
            _ => throw new ArgumentException("Only rhythmverse and encore sources are supported.", paramName),
        };
    }

    public static string? NormalizeTrustedSourceOrNull(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        return NormalizeTrustedSource(source);
    }
}

public sealed record LibraryCatalogEntry(
    string Source,
    string SourceId,
    string? Title,
    string? Artist,
    string? Charter,
    string? LocalPath,
    DateTimeOffset AddedAtUtc,
    string? ExternalKeyHash = null,
    string? InternalIdentityKey = null,
    string? ContentIdentityHash = null);

public sealed class LibraryCatalogService
{
    private readonly string _databasePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LibraryCatalogService(string databasePath)
    {
        _databasePath = databasePath;

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        Initialize();
    }

    public async Task<bool> IsInLibraryAsync(string source, string sourceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(sourceId))
            return false;

        var normalizedSource = LibrarySourceNames.NormalizeTrustedSource(source);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT EXISTS(
                    SELECT 1
                    FROM library_song_keys
                    WHERE source = $source AND source_id = $sourceId
                );
                """;
                command.Parameters.AddWithValue("$source", normalizedSource);
            command.Parameters.AddWithValue("$sourceId", sourceId);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt64(result) == 1;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, bool>> GetMembershipMapAsync(
        string source,
        IEnumerable<string> sourceIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedIds = sourceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (string.IsNullOrWhiteSpace(source) || normalizedIds.Length == 0)
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        var normalizedSource = LibrarySourceNames.NormalizeTrustedSource(source);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            var parameterNames = new List<string>(normalizedIds.Length);
            for (var index = 0; index < normalizedIds.Length; index++)
            {
                var parameterName = "$id" + index;
                parameterNames.Add(parameterName);
                command.Parameters.AddWithValue(parameterName, normalizedIds[index]);
            }

            command.CommandText = $"""
                SELECT source_id
                FROM library_song_keys
                WHERE source = $source
                  AND source_id IN ({string.Join(",", parameterNames)})
                """;
                        command.Parameters.AddWithValue("$source", normalizedSource);

            var map = normalizedIds.ToDictionary(id => id, _ => false, StringComparer.OrdinalIgnoreCase);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                map[reader.GetString(0)] = true;
            }

            return map;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(LibraryCatalogEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.Source) || string.IsNullOrWhiteSpace(entry.SourceId))
            throw new ArgumentException("Source and source identifier are required.", nameof(entry));

        var normalizedSource = LibrarySourceNames.NormalizeTrustedSource(entry.Source, nameof(entry.Source));

        // Only persist key mappings for entries with valid local paths (installed songs)
        if (string.IsNullOrWhiteSpace(entry.LocalPath))
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var nowUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O");

            // First, check if local_path already exists with a different source key
            await using (var checkCommand = connection.CreateCommand())
            {
                checkCommand.CommandText = """
                    SELECT COUNT(*)
                    FROM library_song_keys lsk
                    JOIN library_songs ls ON lsk.library_song_id = ls.id
                    WHERE ls.local_path = $localPath
                      AND NOT (lsk.source = $source AND lsk.source_id = $sourceId);
                    """;
                checkCommand.Parameters.AddWithValue("$localPath", entry.LocalPath);
                                checkCommand.Parameters.AddWithValue("$source", normalizedSource);
                checkCommand.Parameters.AddWithValue("$sourceId", entry.SourceId);

                var count = Convert.ToInt64(await checkCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                if (count > 0)
                {
                    // Local path is already taken by a different source key
                    throw new Microsoft.Data.Sqlite.SqliteException(
                        "UNIQUE constraint failed: library_songs.local_path",
                        19);  // SQLITE_CONSTRAINT
                }
            }

            // Get or create library_songs row
            long librarySongId;
            await using (var songCommand = connection.CreateCommand())
            {
                songCommand.CommandText = """
                    INSERT INTO library_songs (local_path, artist, title, charter, internal_identity_key, content_identity_hash, created_at_utc, updated_at_utc)
                    VALUES ($localPath, $artist, $title, $charter, $internalIdentityKey, $contentIdentityHash, $createdAtUtc, $updatedAtUtc)
                    ON CONFLICT(local_path) DO UPDATE SET
                        artist = excluded.artist,
                        title = excluded.title,
                        charter = excluded.charter,
                        internal_identity_key = excluded.internal_identity_key,
                        content_identity_hash = excluded.content_identity_hash,
                        updated_at_utc = excluded.updated_at_utc
                    RETURNING id;
                    """;
                songCommand.Parameters.AddWithValue("$localPath", entry.LocalPath);
                songCommand.Parameters.AddWithValue("$artist", (object?)entry.Artist ?? DBNull.Value);
                songCommand.Parameters.AddWithValue("$title", (object?)entry.Title ?? DBNull.Value);
                songCommand.Parameters.AddWithValue("$charter", (object?)entry.Charter ?? DBNull.Value);
                songCommand.Parameters.AddWithValue("$internalIdentityKey", (object?)entry.InternalIdentityKey ?? DBNull.Value);
                songCommand.Parameters.AddWithValue("$contentIdentityHash", (object?)entry.ContentIdentityHash ?? DBNull.Value);
                songCommand.Parameters.AddWithValue("$createdAtUtc", entry.AddedAtUtc.UtcDateTime.ToString("O"));
                songCommand.Parameters.AddWithValue("$updatedAtUtc", nowUtc);

                var result = await songCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                librarySongId = Convert.ToInt64(result);
            }

            // Insert or update the key mapping
            await using var keyCommand = connection.CreateCommand();
            keyCommand.CommandText = """
                INSERT INTO library_song_keys (library_song_id, source, source_id, external_key_hash, created_at_utc)
                VALUES ($librarySongId, $source, $sourceId, $externalKeyHash, $createdAtUtc)
                ON CONFLICT(external_key_hash) DO UPDATE SET
                    library_song_id = excluded.library_song_id,
                    source = excluded.source,
                    source_id = excluded.source_id
                """;
            keyCommand.Parameters.AddWithValue("$librarySongId", librarySongId);
            keyCommand.Parameters.AddWithValue("$source", normalizedSource);
            keyCommand.Parameters.AddWithValue("$sourceId", entry.SourceId);
            keyCommand.Parameters.AddWithValue("$externalKeyHash", entry.ExternalKeyHash ?? LibraryIdentityService.BuildExternalKeyHash(entry.SourceId));
            keyCommand.Parameters.AddWithValue("$createdAtUtc", entry.AddedAtUtc.UtcDateTime.ToString("O"));

            await keyCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveOtherEntriesByLocalPathAsync(
        string localPath,
        string keepSource,
        string keepSourceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localPath)
            || string.IsNullOrWhiteSpace(keepSource)
            || string.IsNullOrWhiteSpace(keepSourceId))
            return;

        var normalizedKeepSource = LibrarySourceNames.NormalizeTrustedSource(keepSource, nameof(keepSource));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM library_song_keys
                WHERE library_song_id IN (
                    SELECT id FROM library_songs WHERE local_path = $localPath
                )
                AND NOT (source = $source AND source_id = $sourceId);
                """;
            command.Parameters.AddWithValue("$localPath", localPath);
            command.Parameters.AddWithValue("$source", normalizedKeepSource);
            command.Parameters.AddWithValue("$sourceId", keepSourceId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string source, string sourceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(sourceId))
            return;

        var normalizedSource = LibrarySourceNames.NormalizeTrustedSource(source);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM library_song_keys
                WHERE source = $source AND source_id = $sourceId;
                """;
            command.Parameters.AddWithValue("$source", normalizedSource);
            command.Parameters.AddWithValue("$sourceId", sourceId);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RemoveMissingLocalFilesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var idsToDelete = new List<long>();

            await using (var select = connection.CreateCommand())
            {
                select.CommandText = """
                    SELECT id, local_path
                    FROM library_songs
                    WHERE local_path IS NOT NULL AND local_path != '';
                    """;

                await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var id = reader.GetInt64(0);
                    var localPath = reader.GetString(1);
                    if (!File.Exists(localPath) && !Directory.Exists(localPath))
                        idsToDelete.Add(id);
                }
            }

            if (idsToDelete.Count == 0)
                return 0;

            await using var delete = connection.CreateCommand();
            var paramNames = new List<string>(idsToDelete.Count);
            for (var i = 0; i < idsToDelete.Count; i++)
            {
                var name = "$id" + i;
                paramNames.Add(name);
                delete.Parameters.AddWithValue(name, idsToDelete[i]);
            }

            delete.CommandText = $"DELETE FROM library_songs WHERE id IN ({string.Join(",", paramNames)});";
            return await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RemoveDuplicateLocalPathEntriesUnderRootAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return 0;

        var normalizedRoot = Path.GetFullPath(rootPath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // With the UNIQUE constraint on local_path, duplicates shouldn't exist,
            // but this method is kept for compatibility and defensive cleanup.
            await using var select = connection.CreateCommand();
            select.CommandText = """
                SELECT id, local_path
                FROM library_songs
                WHERE local_path IS NOT NULL AND local_path != '';
                """;

            var idsToDelete = new List<long>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt64(0);
                var localPath = reader.GetString(1);

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(localPath);
                }
                catch
                {
                    continue;
                }

                if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (seenPaths.Contains(fullPath))
                    idsToDelete.Add(id);
                else
                    seenPaths.Add(fullPath);
            }

            if (idsToDelete.Count == 0)
                return 0;

            await using var delete = connection.CreateCommand();
            var parameterNames = new List<string>(idsToDelete.Count);
            for (var i = 0; i < idsToDelete.Count; i++)
            {
                var name = "$id" + i;
                parameterNames.Add(name);
                delete.Parameters.AddWithValue(name, idsToDelete[i]);
            }

            delete.CommandText = $"DELETE FROM library_songs WHERE id IN ({string.Join(",", parameterNames)});";
            return await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> ResolveSourceByLocalPathAsync(string localPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localPath))
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT source
                FROM library_song_keys
                WHERE library_song_id IN (
                    SELECT id FROM library_songs WHERE local_path = $localPath
                )
                ORDER BY created_at_utc DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$localPath", localPath);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is null || result is DBNull ? null : Convert.ToString(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> ResolveSourceIdByLocalPathAsync(string localPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localPath))
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT source_id
                FROM library_song_keys
                WHERE library_song_id IN (
                    SELECT id FROM library_songs WHERE local_path = $localPath
                )
                ORDER BY created_at_utc DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$localPath", localPath);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is null || result is DBNull ? null : Convert.ToString(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LibraryCatalogEntry?> GetEntryByLocalPathAsync(string localPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localPath))
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT k.source, k.source_id, s.title, s.artist, s.charter, s.local_path, k.created_at_utc, k.external_key_hash, s.internal_identity_key, s.content_identity_hash
                FROM library_song_keys k
                JOIN library_songs s ON k.library_song_id = s.id
                WHERE s.local_path = $localPath
                ORDER BY k.created_at_utc DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$localPath", localPath);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;

            return new LibraryCatalogEntry(
                Source: reader.GetString(0),
                SourceId: reader.GetString(1),
                Title: reader.IsDBNull(2) ? null : reader.GetString(2),
                Artist: reader.IsDBNull(3) ? null : reader.GetString(3),
                Charter: reader.IsDBNull(4) ? null : reader.GetString(4),
                LocalPath: reader.IsDBNull(5) ? null : reader.GetString(5),
                AddedAtUtc: DateTimeOffset.Parse(reader.GetString(6)),
                ExternalKeyHash: reader.IsDBNull(7) ? null : reader.GetString(7),
                InternalIdentityKey: reader.IsDBNull(8) ? null : reader.GetString(8),
                ContentIdentityHash: reader.IsDBNull(9) ? null : reader.GetString(9));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetArtistsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT DISTINCT COALESCE(NULLIF(TRIM(artist), ''), 'Unknown Artist') AS artist_name
                FROM library_songs
                WHERE local_path IS NOT NULL AND local_path != ''
                ORDER BY artist_name COLLATE NOCASE ASC;
                """;

            var artists = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                artists.Add(reader.GetString(0));
            }

            return artists;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<LibraryCatalogEntry>> GetEntriesByArtistAsync(string artist, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artist))
            return [];

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT k.source, k.source_id, s.title, s.artist, s.charter, s.local_path, k.created_at_utc, k.external_key_hash, s.internal_identity_key, s.content_identity_hash
                FROM library_song_keys k
                JOIN library_songs s ON k.library_song_id = s.id
                WHERE s.local_path IS NOT NULL
                    AND s.local_path != ''
                    AND COALESCE(NULLIF(TRIM(s.artist), ''), 'Unknown Artist') = $artist
                ORDER BY COALESCE(NULLIF(TRIM(s.title), ''), 'Unknown Song') COLLATE NOCASE ASC;
                """;
            command.Parameters.AddWithValue("$artist", artist);

            var entries = new List<LibraryCatalogEntry>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                entries.Add(new LibraryCatalogEntry(
                    Source: reader.GetString(0),
                    SourceId: reader.GetString(1),
                    Title: reader.IsDBNull(2) ? null : reader.GetString(2),
                    Artist: reader.IsDBNull(3) ? null : reader.GetString(3),
                    Charter: reader.IsDBNull(4) ? null : reader.GetString(4),
                    LocalPath: reader.IsDBNull(5) ? null : reader.GetString(5),
                    AddedAtUtc: DateTimeOffset.Parse(reader.GetString(6)),
                    ExternalKeyHash: reader.IsDBNull(7) ? null : reader.GetString(7),
                    InternalIdentityKey: reader.IsDBNull(8) ? null : reader.GetString(8),
                    ContentIdentityHash: reader.IsDBNull(9) ? null : reader.GetString(9)));
            }

            return entries;
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

        using var command = connection.CreateCommand();
        command.CommandText = """
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

            DELETE FROM library_song_keys
            WHERE lower(source) NOT IN ('rhythmverse', 'encore');

            DELETE FROM library_songs
            WHERE id NOT IN (
                SELECT DISTINCT library_song_id
                FROM library_song_keys
            );
            """;
        command.ExecuteNonQuery();
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
}
