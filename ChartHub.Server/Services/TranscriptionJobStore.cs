using ChartHub.Server.Options;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public enum TranscriptionJobStatus
{
    Queued,
    Claimed,
    Processing,
    Completed,
    Failed,
    Yielded,
}

public enum TranscriptionAggressiveness
{
    Low,
    Medium,
    High,
}

public sealed record TranscriptionJob(
    string JobId,
    string SongId,
    string SongFolderPath,
    TranscriptionAggressiveness Aggressiveness,
    TranscriptionJobStatus Status,
    string? ClaimedByRunnerId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureReason,
    int AttemptNumber);

public sealed record TranscriptionResult(
    string ResultId,
    string JobId,
    string SongId,
    TranscriptionAggressiveness Aggressiveness,
    string MidiFilePath,
    DateTimeOffset CompletedAtUtc,
    bool IsApproved,
    DateTimeOffset? ApprovedAtUtc);

public interface ITranscriptionJobStore
{
    TranscriptionJob CreateJob(string songId, string songFolderPath, TranscriptionAggressiveness aggressiveness, int attemptNumber = 1);

    /// <summary>Atomically claims the next queued job for the runner. Returns null if none available.</summary>
    TranscriptionJob? TryClaimNext(string runnerId);

    void UpdateStatus(string jobId, TranscriptionJobStatus status, string? failureReason = null);

    void MarkCompleted(string jobId, string midiFilePath);

    IReadOnlyList<TranscriptionJob> ListJobs(string? songId = null, TranscriptionJobStatus? status = null);

    TranscriptionJob? GetJob(string jobId);

    bool DeleteJob(string jobId);

    TranscriptionResult? GetLatestApprovedResult(string songId);

    IReadOnlyList<TranscriptionResult> ListResults(string? songId = null);

    void ApproveResult(string resultId);
}

public sealed class TranscriptionJobStore : ITranscriptionJobStore
{
    private readonly string _connectionString;
    private readonly object _syncLock = new();

    public TranscriptionJobStore(IOptions<ServerPathOptions> pathOptions, IWebHostEnvironment environment)
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

    public TranscriptionJob CreateJob(string songId, string songFolderPath, TranscriptionAggressiveness aggressiveness, int attemptNumber = 1)
    {
        string jobId = Guid.NewGuid().ToString("D");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO transcription_jobs (
                    job_id, song_id, song_folder_path, aggressiveness,
                    status, created_at_utc, attempt_number
                ) VALUES (
                    $jobId, $songId, $songFolderPath, $aggressiveness,
                    $status, $createdAt, $attemptNumber
                );
                """;
            cmd.Parameters.AddWithValue("$jobId", jobId);
            cmd.Parameters.AddWithValue("$songId", songId);
            cmd.Parameters.AddWithValue("$songFolderPath", songFolderPath);
            cmd.Parameters.AddWithValue("$aggressiveness", aggressiveness.ToString());
            cmd.Parameters.AddWithValue("$status", nameof(TranscriptionJobStatus.Queued));
            cmd.Parameters.AddWithValue("$createdAt", now.ToString("O"));
            cmd.Parameters.AddWithValue("$attemptNumber", attemptNumber);
            cmd.ExecuteNonQuery();
        }

        return new TranscriptionJob(
            JobId: jobId,
            SongId: songId,
            SongFolderPath: songFolderPath,
            Aggressiveness: aggressiveness,
            Status: TranscriptionJobStatus.Queued,
            ClaimedByRunnerId: null,
            CreatedAtUtc: now,
            ClaimedAtUtc: null,
            CompletedAtUtc: null,
            FailureReason: null,
            AttemptNumber: attemptNumber);
    }

    public TranscriptionJob? TryClaimNext(string runnerId)
    {
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteTransaction tx = conn.BeginTransaction();

            // Pick the oldest queued job not already held by this runner.
            using SqliteCommand select = conn.CreateCommand();
            select.Transaction = tx;
            select.CommandText = $"""
                SELECT {AllColumns}
                FROM transcription_jobs
                WHERE status = 'Queued'
                  AND (claimed_by_runner_id IS NULL OR claimed_by_runner_id != $runnerId)
                ORDER BY created_at_utc ASC
                LIMIT 1;
                """;
            select.Parameters.AddWithValue("$runnerId", runnerId);

            TranscriptionJob? candidate = null;
            using (SqliteDataReader reader = select.ExecuteReader())
            {
                if (reader.Read())
                {
                    candidate = MapJob(reader);
                }
            }

            if (candidate is null)
            {
                tx.Rollback();
                return null;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            using SqliteCommand update = conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = """
                UPDATE transcription_jobs
                SET status = 'Claimed',
                    claimed_by_runner_id = $runnerId,
                    claimed_at_utc = $now
                WHERE job_id = $jobId AND status = 'Queued';
                """;
            update.Parameters.AddWithValue("$runnerId", runnerId);
            update.Parameters.AddWithValue("$now", now.ToString("O"));
            update.Parameters.AddWithValue("$jobId", candidate.JobId);

            int rows = update.ExecuteNonQuery();
            if (rows == 0)
            {
                tx.Rollback();
                return null;
            }

            tx.Commit();
            return candidate with { Status = TranscriptionJobStatus.Claimed, ClaimedByRunnerId = runnerId, ClaimedAtUtc = now };
        }
    }

    public void UpdateStatus(string jobId, TranscriptionJobStatus status, string? failureReason = null)
    {
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE transcription_jobs
                SET status = $status,
                    failure_reason = $failureReason
                WHERE job_id = $jobId;
                """;
            cmd.Parameters.AddWithValue("$status", status.ToString());
            cmd.Parameters.AddWithValue("$failureReason", failureReason is not null ? (object)failureReason : DBNull.Value);
            cmd.Parameters.AddWithValue("$jobId", jobId);
            cmd.ExecuteNonQuery();
        }
    }

    public void MarkCompleted(string jobId, string midiFilePath)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteTransaction tx = conn.BeginTransaction();

            // Fetch current job to get song_id + aggressiveness for the result row.
            using SqliteCommand fetch = conn.CreateCommand();
            fetch.Transaction = tx;
            fetch.CommandText = $"SELECT {AllColumns} FROM transcription_jobs WHERE job_id = $jobId;";
            fetch.Parameters.AddWithValue("$jobId", jobId);

            TranscriptionJob? job = null;
            using (SqliteDataReader reader = fetch.ExecuteReader())
            {
                if (reader.Read())
                {
                    job = MapJob(reader);
                }
            }

            if (job is null)
            {
                tx.Rollback();
                return;
            }

            using SqliteCommand updJob = conn.CreateCommand();
            updJob.Transaction = tx;
            updJob.CommandText = """
                UPDATE transcription_jobs
                SET status = 'Completed', completed_at_utc = $now
                WHERE job_id = $jobId;
                """;
            updJob.Parameters.AddWithValue("$now", now.ToString("O"));
            updJob.Parameters.AddWithValue("$jobId", jobId);
            updJob.ExecuteNonQuery();

            string resultId = Guid.NewGuid().ToString("D");
            using SqliteCommand insResult = conn.CreateCommand();
            insResult.Transaction = tx;
            insResult.CommandText = """
                INSERT INTO transcription_results (
                    result_id, job_id, song_id, aggressiveness, midi_file_path, completed_at_utc
                ) VALUES (
                    $resultId, $jobId, $songId, $aggressiveness, $midiFilePath, $now
                );
                """;
            insResult.Parameters.AddWithValue("$resultId", resultId);
            insResult.Parameters.AddWithValue("$jobId", jobId);
            insResult.Parameters.AddWithValue("$songId", job.SongId);
            insResult.Parameters.AddWithValue("$aggressiveness", job.Aggressiveness.ToString());
            insResult.Parameters.AddWithValue("$midiFilePath", midiFilePath);
            insResult.Parameters.AddWithValue("$now", now.ToString("O"));
            insResult.ExecuteNonQuery();

            tx.Commit();
        }
    }

    public IReadOnlyList<TranscriptionJob> ListJobs(string? songId = null, TranscriptionJobStatus? status = null)
    {
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();

            string where = BuildWhereClause(songId, status, cmd);
            cmd.CommandText = $"SELECT {AllColumns} FROM transcription_jobs{where} ORDER BY created_at_utc DESC;";

            using SqliteDataReader reader = cmd.ExecuteReader();
            List<TranscriptionJob> jobs = [];
            while (reader.Read())
            {
                jobs.Add(MapJob(reader));
            }

            return jobs;
        }
    }

    public TranscriptionJob? GetJob(string jobId)
    {
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {AllColumns} FROM transcription_jobs WHERE job_id = $jobId;";
            cmd.Parameters.AddWithValue("$jobId", jobId);

            using SqliteDataReader reader = cmd.ExecuteReader();
            return reader.Read() ? MapJob(reader) : null;
        }
    }

    public bool DeleteJob(string jobId)
    {
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM transcription_jobs WHERE job_id = $jobId;";
            cmd.Parameters.AddWithValue("$jobId", jobId);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public TranscriptionResult? GetLatestApprovedResult(string songId)
    {
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT {AllResultColumns}
                FROM transcription_results
                WHERE song_id = $songId AND is_approved = 1
                ORDER BY completed_at_utc DESC
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$songId", songId);

            using SqliteDataReader reader = cmd.ExecuteReader();
            return reader.Read() ? MapResult(reader) : null;
        }
    }

    public IReadOnlyList<TranscriptionResult> ListResults(string? songId = null)
    {
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            string where = songId is not null ? " WHERE song_id = $songId" : "";
            if (songId is not null)
            {
                cmd.Parameters.AddWithValue("$songId", songId);
            }

            cmd.CommandText = $"SELECT {AllResultColumns} FROM transcription_results{where} ORDER BY completed_at_utc DESC;";

            using SqliteDataReader reader = cmd.ExecuteReader();
            List<TranscriptionResult> results = [];
            while (reader.Read())
            {
                results.Add(MapResult(reader));
            }

            return results;
        }
    }

    public void ApproveResult(string resultId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE transcription_results
                SET is_approved = 1, approved_at_utc = $now
                WHERE result_id = $resultId;
                """;
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            cmd.Parameters.AddWithValue("$resultId", resultId);
            cmd.ExecuteNonQuery();
        }
    }

    // ---------- schema ----------

    private void EnsureSchema()
    {
        lock (_syncLock)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS transcription_jobs (
                    job_id                  TEXT PRIMARY KEY,
                    song_id                 TEXT NOT NULL,
                    song_folder_path        TEXT NOT NULL,
                    aggressiveness          TEXT NOT NULL,
                    status                  TEXT NOT NULL DEFAULT 'Queued',
                    claimed_by_runner_id    TEXT NULL,
                    created_at_utc          TEXT NOT NULL,
                    claimed_at_utc          TEXT NULL,
                    completed_at_utc        TEXT NULL,
                    failure_reason          TEXT NULL,
                    attempt_number          INTEGER NOT NULL DEFAULT 1
                );

                CREATE INDEX IF NOT EXISTS idx_tj_status ON transcription_jobs(status);
                CREATE INDEX IF NOT EXISTS idx_tj_song_id ON transcription_jobs(song_id);

                CREATE TABLE IF NOT EXISTS transcription_results (
                    result_id           TEXT PRIMARY KEY,
                    job_id              TEXT NOT NULL,
                    song_id             TEXT NOT NULL,
                    aggressiveness      TEXT NOT NULL,
                    midi_file_path      TEXT NOT NULL,
                    completed_at_utc    TEXT NOT NULL,
                    is_approved         INTEGER NOT NULL DEFAULT 0,
                    approved_at_utc     TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_tr_song_id ON transcription_results(song_id);
                """;
            cmd.ExecuteNonQuery();
        }
    }

    // ---------- helpers ----------

    private const string AllColumns =
        "job_id, song_id, song_folder_path, aggressiveness, status, " +
        "claimed_by_runner_id, created_at_utc, claimed_at_utc, completed_at_utc, " +
        "failure_reason, attempt_number";

    private const string AllResultColumns =
        "result_id, job_id, song_id, aggressiveness, midi_file_path, " +
        "completed_at_utc, is_approved, approved_at_utc";

    private static TranscriptionJob MapJob(SqliteDataReader r) =>
        new(
            JobId: r.GetString(0),
            SongId: r.GetString(1),
            SongFolderPath: r.GetString(2),
            Aggressiveness: Enum.Parse<TranscriptionAggressiveness>(r.GetString(3)),
            Status: Enum.Parse<TranscriptionJobStatus>(r.GetString(4)),
            ClaimedByRunnerId: r.IsDBNull(5) ? null : r.GetString(5),
            CreatedAtUtc: DateTimeOffset.Parse(r.GetString(6)),
            ClaimedAtUtc: r.IsDBNull(7) ? null : DateTimeOffset.Parse(r.GetString(7)),
            CompletedAtUtc: r.IsDBNull(8) ? null : DateTimeOffset.Parse(r.GetString(8)),
            FailureReason: r.IsDBNull(9) ? null : r.GetString(9),
            AttemptNumber: r.GetInt32(10));

    private static TranscriptionResult MapResult(SqliteDataReader r) =>
        new(
            ResultId: r.GetString(0),
            JobId: r.GetString(1),
            SongId: r.GetString(2),
            Aggressiveness: Enum.Parse<TranscriptionAggressiveness>(r.GetString(3)),
            MidiFilePath: r.GetString(4),
            CompletedAtUtc: DateTimeOffset.Parse(r.GetString(5)),
            IsApproved: r.GetInt32(6) != 0,
            ApprovedAtUtc: r.IsDBNull(7) ? null : DateTimeOffset.Parse(r.GetString(7)));

    private static string BuildWhereClause(string? songId, TranscriptionJobStatus? status, SqliteCommand cmd)
    {
        List<string> clauses = [];

        if (songId is not null)
        {
            clauses.Add("song_id = $songId");
            cmd.Parameters.AddWithValue("$songId", songId);
        }

        if (status is not null)
        {
            clauses.Add("status = $status");
            cmd.Parameters.AddWithValue("$status", status.ToString());
        }

        return clauses.Count == 0 ? "" : " WHERE " + string.Join(" AND ", clauses);
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        return connection;
    }
}
