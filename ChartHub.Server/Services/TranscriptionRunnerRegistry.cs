using System.Security.Cryptography;

using ChartHub.Server.Contracts;
using ChartHub.Server.Options;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public interface ITranscriptionRunnerRegistry
{
    /// <summary>Issues a one-time registration token. Expires after <paramref name="ttl"/>.</summary>
    RunnerRegistrationTokenResponse IssueRegistrationToken(TimeSpan ttl);

    /// <summary>Exchanges a one-time token for a durable runner identity.</summary>
    RegisterRunnerResponse RegisterRunner(string runnerName, string plainToken, string plainSecret, int maxConcurrency);

    /// <summary>Validates runner credentials. Returns the runner row on success; null on failure.</summary>
    TranscriptionRunnerRecord? ValidateRunner(string runnerId, string plainSecret);

    /// <summary>Records a heartbeat so the runner shows as online.</summary>
    void RecordHeartbeat(string runnerId, int activeJobCount);

    IReadOnlyList<TranscriptionRunnerRecord> ListRunners();

    bool TryDeregisterRunner(string runnerId);
}

public sealed record TranscriptionRunnerRecord(
    string RunnerId,
    string RunnerName,
    int MaxConcurrency,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? LastHeartbeatUtc,
    int? LastActiveJobCount,
    bool IsActive);

public sealed class TranscriptionRunnerRegistry : ITranscriptionRunnerRegistry
{
    private readonly string _connectionString;
    private readonly object _syncLock = new();

    public TranscriptionRunnerRegistry(IOptions<ServerPathOptions> pathOptions, IWebHostEnvironment environment)
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

        EnsureSchema();
    }

    public RunnerRegistrationTokenResponse IssueRegistrationToken(TimeSpan ttl)
    {
        string plainToken = GenerateSecureToken(32);
        string tokenId = Guid.NewGuid().ToString("D");
        string tokenHash = BCrypt.Net.BCrypt.HashPassword(plainToken, workFactor: 10);
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.Add(ttl);

        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO runner_registration_tokens (
                    token_id, token_hash, expires_at_utc
                ) VALUES ($tokenId, $tokenHash, $expiresAt);
                """;
            cmd.Parameters.AddWithValue("$tokenId", tokenId);
            cmd.Parameters.AddWithValue("$tokenHash", tokenHash);
            cmd.Parameters.AddWithValue("$expiresAt", expiresAt.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        return new RunnerRegistrationTokenResponse(tokenId, plainToken, expiresAt);
    }

    public RegisterRunnerResponse RegisterRunner(string runnerName, string plainToken, string plainSecret, int maxConcurrency)
    {
        if (string.IsNullOrWhiteSpace(runnerName))
        {
            throw new ArgumentException("Runner name must not be blank.", nameof(runnerName));
        }

        if (maxConcurrency < 1 || maxConcurrency > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Max concurrency must be between 1 and 16.");
        }

        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();

            // Find an unconsumed, unexpired token that matches.
            using SqliteCommand findToken = conn.CreateCommand();
            findToken.CommandText = """
                SELECT token_id, token_hash
                FROM runner_registration_tokens
                WHERE consumed_at_utc IS NULL
                  AND expires_at_utc > $now
                ORDER BY expires_at_utc ASC;
                """;
            findToken.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));

            string? matchedTokenId = null;
            using (SqliteDataReader reader = findToken.ExecuteReader())
            {
                while (reader.Read())
                {
                    string candidateId = reader.GetString(0);
                    string candidateHash = reader.GetString(1);
                    if (BCrypt.Net.BCrypt.Verify(plainToken, candidateHash))
                    {
                        matchedTokenId = candidateId;
                        break;
                    }
                }
            }

            if (matchedTokenId is null)
            {
                throw new UnauthorizedAccessException("Invalid or expired registration token.");
            }

            string runnerId = Guid.NewGuid().ToString("D");
            string secretHash = BCrypt.Net.BCrypt.HashPassword(plainSecret, workFactor: 10);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            using SqliteCommand insertRunner = conn.CreateCommand();
            insertRunner.CommandText = """
                INSERT INTO transcription_runners (
                    runner_id, runner_name, secret_hash, max_concurrency,
                    registered_at_utc, is_active, registration_token_id
                ) VALUES (
                    $runnerId, $runnerName, $secretHash, $maxConcurrency,
                    $registeredAt, 1, $tokenId
                );
                """;
            insertRunner.Parameters.AddWithValue("$runnerId", runnerId);
            insertRunner.Parameters.AddWithValue("$runnerName", runnerName);
            insertRunner.Parameters.AddWithValue("$secretHash", secretHash);
            insertRunner.Parameters.AddWithValue("$maxConcurrency", maxConcurrency);
            insertRunner.Parameters.AddWithValue("$registeredAt", now.ToString("O"));
            insertRunner.Parameters.AddWithValue("$tokenId", matchedTokenId);
            insertRunner.ExecuteNonQuery();

            // Consume the token.
            using SqliteCommand consumeToken = conn.CreateCommand();
            consumeToken.CommandText = """
                UPDATE runner_registration_tokens
                SET consumed_at_utc = $now, consumed_by_runner_id = $runnerId
                WHERE token_id = $tokenId;
                """;
            consumeToken.Parameters.AddWithValue("$now", now.ToString("O"));
            consumeToken.Parameters.AddWithValue("$runnerId", runnerId);
            consumeToken.Parameters.AddWithValue("$tokenId", matchedTokenId);
            consumeToken.ExecuteNonQuery();

            return new RegisterRunnerResponse(runnerId);
        }
    }

    public TranscriptionRunnerRecord? ValidateRunner(string runnerId, string plainSecret)
    {
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT runner_id, runner_name, secret_hash, max_concurrency,
                       registered_at_utc, last_heartbeat_utc, last_active_job_count, is_active
                FROM transcription_runners
                WHERE runner_id = $runnerId AND is_active = 1;
                """;
            cmd.Parameters.AddWithValue("$runnerId", runnerId);

            using SqliteDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            string storedHash = reader.GetString(2);
            if (!BCrypt.Net.BCrypt.Verify(plainSecret, storedHash))
            {
                return null;
            }

            return MapRunner(reader);
        }
    }

    public void RecordHeartbeat(string runnerId, int activeJobCount)
    {
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE transcription_runners
                SET last_heartbeat_utc = $now, last_active_job_count = $jobCount
                WHERE runner_id = $runnerId;
                """;
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$jobCount", activeJobCount);
            cmd.Parameters.AddWithValue("$runnerId", runnerId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<TranscriptionRunnerRecord> ListRunners()
    {
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT runner_id, runner_name, secret_hash, max_concurrency,
                       registered_at_utc, last_heartbeat_utc, last_active_job_count, is_active
                FROM transcription_runners
                ORDER BY registered_at_utc DESC;
                """;

            using SqliteDataReader reader = cmd.ExecuteReader();
            List<TranscriptionRunnerRecord> records = [];
            while (reader.Read())
            {
                records.Add(MapRunner(reader));
            }

            return records;
        }
    }

    public bool TryDeregisterRunner(string runnerId)
    {
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE transcription_runners SET is_active = 0
                WHERE runner_id = $runnerId AND is_active = 1;
                """;
            cmd.Parameters.AddWithValue("$runnerId", runnerId);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    private static TranscriptionRunnerRecord MapRunner(SqliteDataReader reader)
    {
        return new TranscriptionRunnerRecord(
            RunnerId: reader.GetString(0),
            RunnerName: reader.GetString(1),
            MaxConcurrency: reader.GetInt32(3),
            RegisteredAtUtc: DateTimeOffset.Parse(reader.GetString(4)),
            LastHeartbeatUtc: reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)),
            LastActiveJobCount: reader.IsDBNull(6) ? null : reader.GetInt32(6),
            IsActive: reader.GetInt32(7) != 0);
    }

    private void EnsureSchema()
    {
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS transcription_runners (
                    runner_id               TEXT PRIMARY KEY,
                    runner_name             TEXT NOT NULL,
                    secret_hash             TEXT NOT NULL,
                    max_concurrency         INTEGER NOT NULL DEFAULT 1,
                    registered_at_utc       TEXT NOT NULL,
                    last_heartbeat_utc      TEXT NULL,
                    last_active_job_count   INTEGER NULL,
                    is_active               INTEGER NOT NULL DEFAULT 1,
                    registration_token_id   TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS runner_registration_tokens (
                    token_id                TEXT PRIMARY KEY,
                    token_hash              TEXT NOT NULL,
                    expires_at_utc          TEXT NOT NULL,
                    consumed_at_utc         TEXT NULL,
                    consumed_by_runner_id   TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_reg_tokens_expires
                    ON runner_registration_tokens(expires_at_utc);
                """;
            cmd.ExecuteNonQuery();
        }
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        return connection;
    }

    private static string GenerateSecureToken(int byteLength)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
