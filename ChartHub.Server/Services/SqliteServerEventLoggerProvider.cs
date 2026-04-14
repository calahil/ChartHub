using System.Text;

using ChartHub.Server.Options;

using Microsoft.Data.Sqlite;

namespace ChartHub.Server.Services;

/// <summary>
/// Writes all server log entries to the <c>server_events</c> SQLite table.
/// Registered alongside <see cref="ServerFileLoggerProvider"/> for structured log persistence.
/// </summary>
public sealed class SqliteServerEventLoggerProvider : ILoggerProvider
{
    private readonly string _connectionString;
    private readonly object _syncLock = new();

    public SqliteServerEventLoggerProvider(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        EnsureSchema();
    }

    public ILogger CreateLogger(string categoryName) => new SqliteServerEventLogger(categoryName, this);

    public void Dispose()
    {
    }

    private void EnsureSchema()
    {
        lock (_syncLock)
        {
            string? dbDir = Path.GetDirectoryName(_connectionString
                .Split(';')
                .FirstOrDefault(p => p.Trim().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                ?.Split('=', 2)[1]
                .Trim());

            if (!string.IsNullOrWhiteSpace(dbDir))
            {
                Directory.CreateDirectory(dbDir);
            }

            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS server_events (
                    event_pk INTEGER PRIMARY KEY AUTOINCREMENT,
                    log_level TEXT NOT NULL,
                    event_id INTEGER NOT NULL DEFAULT 0,
                    category TEXT NULL,
                    message TEXT NOT NULL,
                    exception TEXT NULL,
                    timestamp_utc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    internal void WriteLog(LogLevel level, EventId eventId, string categoryName, string message, Exception? exception)
    {
        lock (_syncLock)
        {
            try
            {
                using SqliteConnection connection = OpenConnection();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO server_events (log_level, event_id, category, message, exception, timestamp_utc)
                    VALUES ($logLevel, $eventId, $category, $message, $exception, $timestamp);
                    """;
                command.Parameters.AddWithValue("$logLevel", level.ToString());
                command.Parameters.AddWithValue("$eventId", eventId.Id);
                command.Parameters.AddWithValue("$category", string.IsNullOrWhiteSpace(categoryName) ? DBNull.Value : (object)categoryName);
                command.Parameters.AddWithValue("$message", message);
                command.Parameters.AddWithValue("$exception", exception is null ? DBNull.Value : (object)exception.ToString());
                command.Parameters.AddWithValue("$timestamp", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }
            catch
            {
                // Degrade gracefully if the DB is unavailable; file logger remains active.
            }
        }
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        return connection;
    }

    private sealed class SqliteServerEventLogger(string categoryName, SqliteServerEventLoggerProvider provider) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            provider.WriteLog(logLevel, eventId, categoryName, message, exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
