using Microsoft.Data.Sqlite;

namespace ChartHub.Services;

public static class LibrarySourceNames
{
    public const string RhythmVerse = "rhythmverse";
    public const string Encore = "encore";
}

public sealed record LibraryCatalogEntry(
    string Source,
    string SourceId,
    string? Title,
    string? Artist,
    string? Charter,
    string? LocalPath,
    DateTimeOffset AddedAtUtc);

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

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT EXISTS(
                    SELECT 1
                    FROM library_entries
                    WHERE source = $source AND source_id = $sourceId
                );
                """;
            command.Parameters.AddWithValue("$source", source);
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
                FROM library_entries
                WHERE source = $source
                  AND source_id IN ({string.Join(",", parameterNames)})
                """;
            command.Parameters.AddWithValue("$source", source);

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

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO library_entries (
                    source,
                    source_id,
                    title,
                    artist,
                    charter,
                    local_path,
                    added_at_utc
                )
                VALUES (
                    $source,
                    $sourceId,
                    $title,
                    $artist,
                    $charter,
                    $localPath,
                    $addedAtUtc
                )
                ON CONFLICT(source, source_id) DO UPDATE SET
                    title = excluded.title,
                    artist = excluded.artist,
                    charter = excluded.charter,
                    local_path = excluded.local_path,
                    added_at_utc = excluded.added_at_utc;
                """;
            command.Parameters.AddWithValue("$source", entry.Source);
            command.Parameters.AddWithValue("$sourceId", entry.SourceId);
            command.Parameters.AddWithValue("$title", (object?)entry.Title ?? DBNull.Value);
            command.Parameters.AddWithValue("$artist", (object?)entry.Artist ?? DBNull.Value);
            command.Parameters.AddWithValue("$charter", (object?)entry.Charter ?? DBNull.Value);
            command.Parameters.AddWithValue("$localPath", (object?)entry.LocalPath ?? DBNull.Value);
            command.Parameters.AddWithValue("$addedAtUtc", entry.AddedAtUtc.UtcDateTime.ToString("O"));

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

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM library_entries
                WHERE source = $source AND source_id = $sourceId;
                """;
            command.Parameters.AddWithValue("$source", source);
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
                    FROM library_entries
                    WHERE local_path IS NOT NULL AND local_path != '';
                    """;

                await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var id = reader.GetInt64(0);
                    var localPath = reader.GetString(1);
                    if (!File.Exists(localPath))
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

            delete.CommandText = $"DELETE FROM library_entries WHERE id IN ({string.Join(",", paramNames)});";
            return await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
            CREATE TABLE IF NOT EXISTS library_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source TEXT NOT NULL,
                source_id TEXT NOT NULL,
                title TEXT NULL,
                artist TEXT NULL,
                charter TEXT NULL,
                local_path TEXT NULL,
                added_at_utc TEXT NOT NULL,
                UNIQUE(source, source_id)
            );

            CREATE INDEX IF NOT EXISTS idx_library_entries_source_source_id
                ON library_entries(source, source_id);
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