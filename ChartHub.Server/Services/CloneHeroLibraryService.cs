using System.Security.Cryptography;
using System.Text;

using ChartHub.Server.Contracts;
using ChartHub.Server.Options;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed record CloneHeroLibraryUpsertRequest(
    string Source,
    string SourceId,
    string Artist,
    string Title,
    string Charter,
    string? SourceMd5,
    string? SourceChartHash,
    string? SourceUrl,
    string InstalledPath,
    string InstalledRelativePath);

public interface ICloneHeroLibraryService
{
    IReadOnlyList<CloneHeroSongResponse> ListSongs();

    bool TryGetSong(string songId, out CloneHeroSongResponse? song);

    bool TrySoftDeleteSong(string songId, out CloneHeroSongResponse? song);

    bool TryRestoreSong(string songId, out CloneHeroSongResponse? song);

    void UpsertInstalledSong(CloneHeroLibraryUpsertRequest request);
}

public sealed class CloneHeroLibraryService : ICloneHeroLibraryService
{
    private readonly string _connectionString;
    private readonly IServerCloneHeroDirectorySchemaService _schemaService;
    private readonly object _sync = new();

    public CloneHeroLibraryService(
        IOptions<ServerPathOptions> pathOptions,
        IWebHostEnvironment environment,
        IServerCloneHeroDirectorySchemaService schemaService)
    {
        _schemaService = schemaService;

        string dbPath = pathOptions.Value.SqliteDbPath;
        if (!Path.IsPathRooted(dbPath))
        {
            dbPath = Path.Combine(environment.ContentRootPath, dbPath);
        }

        string? dbDirectory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dbDirectory))
        {
            Directory.CreateDirectory(dbDirectory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        EnsureSchema();
    }

    public IReadOnlyList<CloneHeroSongResponse> ListSongs()
    {
        lock (_sync)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    song_id,
                    source,
                    source_id,
                    artist,
                    title,
                    charter,
                    source_md5,
                    source_chart_hash,
                    source_url,
                    installed_path,
                    installed_relative_path,
                    updated_at_utc
                FROM clonehero_library
                WHERE is_deleted = 0
                ORDER BY updated_at_utc DESC;
                """;

            using SqliteDataReader reader = command.ExecuteReader();
            List<CloneHeroSongResponse> songs = [];
            while (reader.Read())
            {
                songs.Add(Map(reader));
            }

            return songs;
        }
    }

    public bool TryGetSong(string songId, out CloneHeroSongResponse? song)
    {
        lock (_sync)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    song_id,
                    source,
                    source_id,
                    artist,
                    title,
                    charter,
                    source_md5,
                    source_chart_hash,
                    source_url,
                    installed_path,
                    installed_relative_path,
                    updated_at_utc
                FROM clonehero_library
                WHERE song_id = $songId
                  AND is_deleted = 0
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$songId", songId);

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                song = null;
                return false;
            }

            song = Map(reader);
            return true;
        }
    }

    public bool TrySoftDeleteSong(string songId, out CloneHeroSongResponse? song)
    {
        lock (_sync)
        {
            if (!TryGetSong(songId, out CloneHeroSongResponse? existing) || existing is null)
            {
                song = null;
                return false;
            }

            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE clonehero_library
                SET
                    is_deleted = 1,
                    updated_at_utc = $updatedAtUtc
                WHERE song_id = $songId;
                """;
            command.Parameters.AddWithValue("$songId", songId);
            command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();

            song = existing;
            return true;
        }
    }

    public bool TryRestoreSong(string songId, out CloneHeroSongResponse? song)
    {
        lock (_sync)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand restoreCommand = connection.CreateCommand();
            restoreCommand.CommandText = """
                UPDATE clonehero_library
                SET
                    is_deleted = 0,
                    updated_at_utc = $updatedAtUtc
                WHERE song_id = $songId
                  AND is_deleted = 1;
                """;
            restoreCommand.Parameters.AddWithValue("$songId", songId);
            restoreCommand.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            int affected = restoreCommand.ExecuteNonQuery();
            if (affected <= 0)
            {
                song = null;
                return false;
            }

            return TryGetSong(songId, out song);
        }
    }

    public void UpsertInstalledSong(CloneHeroLibraryUpsertRequest request)
    {
        lock (_sync)
        {
            string normalizedSource = _schemaService.NormalizeSource(request.Source);
            string sourceId = string.IsNullOrWhiteSpace(request.SourceId)
                ? "unknown"
                : request.SourceId.Trim();
            string songId = BuildSongId(normalizedSource, sourceId);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO clonehero_library (
                    song_id,
                    source,
                    source_id,
                    artist,
                    title,
                    charter,
                    source_md5,
                    source_chart_hash,
                    source_url,
                    installed_path,
                    installed_relative_path,
                    is_deleted,
                    created_at_utc,
                    updated_at_utc
                ) VALUES (
                    $songId,
                    $source,
                    $sourceId,
                    $artist,
                    $title,
                    $charter,
                    $sourceMd5,
                    $sourceChartHash,
                    $sourceUrl,
                    $installedPath,
                    $installedRelativePath,
                    0,
                    $createdAtUtc,
                    $updatedAtUtc
                )
                ON CONFLICT(song_id) DO UPDATE SET
                    source = excluded.source,
                    source_id = excluded.source_id,
                    artist = excluded.artist,
                    title = excluded.title,
                    charter = excluded.charter,
                    source_md5 = excluded.source_md5,
                    source_chart_hash = excluded.source_chart_hash,
                    source_url = excluded.source_url,
                    installed_path = excluded.installed_path,
                    installed_relative_path = excluded.installed_relative_path,
                    is_deleted = 0,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$songId", songId);
            command.Parameters.AddWithValue("$source", normalizedSource);
            command.Parameters.AddWithValue("$sourceId", sourceId);
            command.Parameters.AddWithValue("$artist", NormalizeMetadata(request.Artist, "Unknown Artist"));
            command.Parameters.AddWithValue("$title", NormalizeMetadata(request.Title, "Unknown Song"));
            command.Parameters.AddWithValue("$charter", NormalizeMetadata(request.Charter, "Unknown Charter"));
            command.Parameters.AddWithValue("$sourceMd5", DbValueOrNull(request.SourceMd5));
            command.Parameters.AddWithValue("$sourceChartHash", DbValueOrNull(request.SourceChartHash));
            command.Parameters.AddWithValue("$sourceUrl", DbValueOrNull(request.SourceUrl));
            command.Parameters.AddWithValue("$installedPath", request.InstalledPath);
            command.Parameters.AddWithValue("$installedRelativePath", request.InstalledRelativePath);
            command.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
            command.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    private void EnsureSchema()
    {
        lock (_sync)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS clonehero_library (
                    song_id TEXT PRIMARY KEY,
                    source TEXT NOT NULL,
                    source_id TEXT NOT NULL,
                    artist TEXT NOT NULL,
                    title TEXT NOT NULL,
                    charter TEXT NOT NULL,
                    source_md5 TEXT NULL,
                    source_chart_hash TEXT NULL,
                    source_url TEXT NULL,
                    installed_path TEXT NOT NULL,
                    installed_relative_path TEXT NOT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS idx_clonehero_library_source_lookup
                    ON clonehero_library(source, source_id);
                CREATE INDEX IF NOT EXISTS idx_clonehero_library_updated_at
                    ON clonehero_library(updated_at_utc);
                """;
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        return connection;
    }

    private static CloneHeroSongResponse Map(SqliteDataReader reader)
    {
        return new CloneHeroSongResponse
        {
            SongId = reader.GetString(0),
            Source = reader.GetString(1),
            SourceId = reader.GetString(2),
            Artist = reader.GetString(3),
            Title = reader.GetString(4),
            Charter = reader.GetString(5),
            SourceMd5 = reader.IsDBNull(6) ? null : reader.GetString(6),
            SourceChartHash = reader.IsDBNull(7) ? null : reader.GetString(7),
            SourceUrl = reader.IsDBNull(8) ? null : reader.GetString(8),
            InstalledPath = reader.IsDBNull(9) ? null : reader.GetString(9),
            InstalledRelativePath = reader.IsDBNull(10) ? null : reader.GetString(10),
            UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(11)),
        };
    }

    private static string BuildSongId(string source, string sourceId)
    {
        string combined = $"{source}|{sourceId}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static object DbValueOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DBNull.Value
            : value.Trim();
    }

    private static string NormalizeMetadata(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}
