using ChartHub.Server.Options;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed class SqliteJobLogSink : IJobLogSink
{
    private readonly string _connectionString;
    private readonly object _syncLock = new();

    public SqliteJobLogSink(IOptions<ServerPathOptions> pathOptions, IWebHostEnvironment environment)
    {
        string dbPath = pathOptions.Value.SqliteDbPath;
        if (!Path.IsPathRooted(dbPath))
        {
            dbPath = Path.Combine(environment.ContentRootPath, dbPath);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        // Tables are created by SqliteDownloadJobStore.EnsureSchema which runs before any log writes.
        // We ensure separately so job log sink can operate safely even if store initialization order changes.
        EnsureLogTable();
    }

    public void Add(Guid jobId, LogLevel level, EventId eventId, string? category, string message, string? exception)
    {
        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO job_logs (job_id, log_level, event_id, category, message, exception, timestamp_utc)
                VALUES ($jobId, $logLevel, $eventId, $category, $message, $exception, $timestamp);
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            command.Parameters.AddWithValue("$logLevel", level.ToString());
            command.Parameters.AddWithValue("$eventId", eventId.Id);
            command.Parameters.AddWithValue("$category", (object?)category ?? DBNull.Value);
            command.Parameters.AddWithValue("$message", message);
            command.Parameters.AddWithValue("$exception", (object?)exception ?? DBNull.Value);
            command.Parameters.AddWithValue("$timestamp", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<JobLogEntry> GetLogs(Guid jobId)
    {
        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT timestamp_utc, log_level, event_id, category, message, exception
                FROM job_logs
                WHERE job_id = $jobId
                ORDER BY log_entry_id ASC;
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));

            using SqliteDataReader reader = command.ExecuteReader();
            var entries = new List<JobLogEntry>();
            while (reader.Read())
            {
                entries.Add(new JobLogEntry(
                    DateTimeOffset.Parse(reader.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }

            return entries;
        }
    }

    private void EnsureLogTable()
    {
        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS job_logs (
                    log_entry_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    job_id TEXT NOT NULL,
                    log_level TEXT NOT NULL,
                    event_id INTEGER NOT NULL DEFAULT 0,
                    category TEXT NULL,
                    message TEXT NOT NULL,
                    exception TEXT NULL,
                    timestamp_utc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_job_logs_job_id ON job_logs(job_id);
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
}
