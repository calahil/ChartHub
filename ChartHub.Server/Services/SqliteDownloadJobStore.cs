using ChartHub.Server.Contracts;
using ChartHub.Server.Options;

using Microsoft.Data.Sqlite;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public interface IDownloadJobStore
{
    DownloadJobResponse Create(CreateDownloadJobRequest request);

    IReadOnlyList<DownloadJobResponse> List();

    bool TryGet(Guid jobId, out DownloadJobResponse? response);

    void QueueRetry(Guid jobId);

    void RequestCancel(Guid jobId);

    bool IsCancelRequested(Guid jobId);

    DownloadJobResponse? TryClaimNextQueuedJob();

    void UpdateProgress(Guid jobId, string stage, double progressPercent);

    void MarkDownloaded(Guid jobId, string downloadedPath);

    void MarkStaged(Guid jobId, string stagedPath);

    void MarkInstalled(Guid jobId, string installedPath);

    void MarkCancelled(Guid jobId);

    void MarkFailed(Guid jobId, string errorMessage);

    int DeleteFinishedOlderThan(DateTimeOffset thresholdUtc);
}

public sealed class SqliteDownloadJobStore : IDownloadJobStore
{
    private readonly string _connectionString;
    private readonly object _syncLock = new();

    public SqliteDownloadJobStore(IOptions<ServerPathOptions> pathOptions, IWebHostEnvironment environment)
    {
        string dbPath = pathOptions.Value.SqliteDbPath;
        if (!Path.IsPathRooted(dbPath))
        {
            dbPath = Path.Combine(environment.ContentRootPath, dbPath);
        }
        string? dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        EnsureSchema();
    }

    public DownloadJobResponse Create(CreateDownloadJobRequest request)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();

        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO download_jobs (
                    job_id,
                    source,
                    source_id,
                    display_name,
                    source_url,
                    stage,
                    progress_percent,
                    cancel_requested,
                    created_at_utc,
                    updated_at_utc
                ) VALUES (
                    $jobId,
                    $source,
                    $sourceId,
                    $displayName,
                    $sourceUrl,
                    $stage,
                    $progressPercent,
                    0,
                    $createdAtUtc,
                    $updatedAtUtc
                );
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            command.Parameters.AddWithValue("$source", request.Source);
            command.Parameters.AddWithValue("$sourceId", request.SourceId);
            command.Parameters.AddWithValue("$displayName", request.DisplayName);
            command.Parameters.AddWithValue("$sourceUrl", request.SourceUrl);
            command.Parameters.AddWithValue("$stage", "Queued");
            command.Parameters.AddWithValue("$progressPercent", 0d);
            command.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
            command.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            command.ExecuteNonQuery();
        }

        return GetRequired(jobId);
    }

    public IReadOnlyList<DownloadJobResponse> List()
    {
        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    job_id,
                    source,
                    source_id,
                    display_name,
                    source_url,
                    stage,
                    progress_percent,
                    downloaded_path,
                    staged_path,
                    installed_path,
                    error,
                    created_at_utc,
                    updated_at_utc
                FROM download_jobs
                ORDER BY updated_at_utc DESC;
                """;

            using SqliteDataReader reader = command.ExecuteReader();
            List<DownloadJobResponse> jobs = [];
            while (reader.Read())
            {
                jobs.Add(Map(reader));
            }

            return jobs;
        }
    }

    public bool TryGet(Guid jobId, out DownloadJobResponse? response)
    {
        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    job_id,
                    source,
                    source_id,
                    display_name,
                    source_url,
                    stage,
                    progress_percent,
                    downloaded_path,
                    staged_path,
                    installed_path,
                    error,
                    created_at_utc,
                    updated_at_utc
                FROM download_jobs
                WHERE job_id = $jobId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                response = null;
                return false;
            }

            response = Map(reader);
            return true;
        }
    }

    public void QueueRetry(Guid jobId)
    {
        Mutate(jobId, "Queued", 0d, null, clearPaths: false, cancelRequested: false);
    }

    public void RequestCancel(Guid jobId)
    {
        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE download_jobs
                SET
                    cancel_requested = 1,
                    stage = $stage,
                    updated_at_utc = $updatedAtUtc
                WHERE job_id = $jobId;
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            command.Parameters.AddWithValue("$stage", "Cancelling");
            command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public bool IsCancelRequested(Guid jobId)
    {
        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT cancel_requested FROM download_jobs WHERE job_id = $jobId LIMIT 1;";
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));

            object? value = command.ExecuteScalar();
            if (value is null || value is DBNull)
            {
                return false;
            }

            return Convert.ToInt32(value) == 1;
        }
    }

    public DownloadJobResponse? TryClaimNextQueuedJob()
    {
        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            using SqliteCommand select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = """
                SELECT
                    job_id,
                    source,
                    source_id,
                    display_name,
                    source_url,
                    stage,
                    progress_percent,
                    downloaded_path,
                    staged_path,
                    installed_path,
                    error,
                    created_at_utc,
                    updated_at_utc
                FROM download_jobs
                WHERE stage = 'Queued'
                ORDER BY created_at_utc ASC
                LIMIT 1;
                """;

            using SqliteDataReader reader = select.ExecuteReader();
            if (!reader.Read())
            {
                transaction.Commit();
                return null;
            }

            DownloadJobResponse claimed = Map(reader);
            reader.Close();

            using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE download_jobs
                SET
                    stage = 'ResolvingSource',
                    progress_percent = 1,
                    cancel_requested = 0,
                    error = NULL,
                    updated_at_utc = $updatedAtUtc
                WHERE job_id = $jobId;
                """;
            update.Parameters.AddWithValue("$jobId", claimed.JobId.ToString("D"));
            update.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            update.ExecuteNonQuery();

            transaction.Commit();
            return GetRequired(claimed.JobId);
        }
    }

    public void UpdateProgress(Guid jobId, string stage, double progressPercent)
    {
        Mutate(jobId, stage, progressPercent, null, clearPaths: false, cancelRequested: null);
    }

    public void MarkDownloaded(Guid jobId, string downloadedPath)
    {
        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE download_jobs
                SET
                    downloaded_path = $downloadedPath,
                    stage = 'Downloaded',
                    progress_percent = 80,
                    updated_at_utc = $updatedAtUtc
                WHERE job_id = $jobId;
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            command.Parameters.AddWithValue("$downloadedPath", downloadedPath);
            command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public void MarkStaged(Guid jobId, string stagedPath)
    {
        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE download_jobs
                SET
                    staged_path = $stagedPath,
                    stage = 'Staged',
                    progress_percent = 90,
                    updated_at_utc = $updatedAtUtc
                WHERE job_id = $jobId;
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            command.Parameters.AddWithValue("$stagedPath", stagedPath);
            command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public void MarkInstalled(Guid jobId, string installedPath)
    {
        lock (_syncLock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE download_jobs
                SET
                    installed_path = $installedPath,
                    stage = 'Completed',
                    progress_percent = 100,
                    completed_at_utc = $completedAtUtc,
                    updated_at_utc = $updatedAtUtc
                WHERE job_id = $jobId;
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            command.Parameters.AddWithValue("$installedPath", installedPath);
            command.Parameters.AddWithValue("$completedAtUtc", now.ToString("O"));
            command.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public void MarkCancelled(Guid jobId)
    {
        lock (_syncLock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE download_jobs
                SET
                    stage = 'Cancelled',
                    progress_percent = 100,
                    completed_at_utc = $completedAtUtc,
                    updated_at_utc = $updatedAtUtc
                WHERE job_id = $jobId;
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            command.Parameters.AddWithValue("$completedAtUtc", now.ToString("O"));
            command.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public void MarkFailed(Guid jobId, string errorMessage)
    {
        lock (_syncLock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE download_jobs
                SET
                    error = $error,
                    stage = 'Failed',
                    progress_percent = 100,
                    completed_at_utc = $completedAtUtc,
                    updated_at_utc = $updatedAtUtc
                WHERE job_id = $jobId;
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            command.Parameters.AddWithValue("$error", errorMessage);
            command.Parameters.AddWithValue("$completedAtUtc", now.ToString("O"));
            command.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    public int DeleteFinishedOlderThan(DateTimeOffset thresholdUtc)
    {
        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM download_jobs
                WHERE stage IN ('Completed', 'Failed', 'Cancelled')
                  AND completed_at_utc IS NOT NULL
                  AND completed_at_utc < $thresholdUtc;
                """;
            command.Parameters.AddWithValue("$thresholdUtc", thresholdUtc.ToString("O"));
            return command.ExecuteNonQuery();
        }
    }

    private void EnsureSchema()
    {
        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS download_jobs (
                    job_id TEXT PRIMARY KEY,
                    source TEXT NOT NULL,
                    source_id TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    source_url TEXT NOT NULL,
                    stage TEXT NOT NULL,
                    progress_percent REAL NOT NULL,
                    cancel_requested INTEGER NOT NULL DEFAULT 0,
                    downloaded_path TEXT NULL,
                    staged_path TEXT NULL,
                    installed_path TEXT NULL,
                    error TEXT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    completed_at_utc TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_download_jobs_stage ON download_jobs(stage);
                CREATE INDEX IF NOT EXISTS idx_download_jobs_updated ON download_jobs(updated_at_utc);
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

    private DownloadJobResponse GetRequired(Guid jobId)
    {
        if (!TryGet(jobId, out DownloadJobResponse? response) || response is null)
        {
            throw new InvalidOperationException($"Download job '{jobId:D}' was expected but not found.");
        }

        return response;
    }

    private void Mutate(Guid jobId, string stage, double progressPercent, string? error, bool clearPaths, bool? cancelRequested)
    {
        lock (_syncLock)
        {
            using SqliteConnection connection = OpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE download_jobs
                SET
                    stage = $stage,
                    progress_percent = $progressPercent,
                    error = $error,
                    cancel_requested = COALESCE($cancelRequested, cancel_requested),
                    downloaded_path = CASE WHEN $clearPaths = 1 THEN NULL ELSE downloaded_path END,
                    staged_path = CASE WHEN $clearPaths = 1 THEN NULL ELSE staged_path END,
                    installed_path = CASE WHEN $clearPaths = 1 THEN NULL ELSE installed_path END,
                    completed_at_utc = CASE WHEN $stage IN ('Completed', 'Failed', 'Cancelled') THEN COALESCE(completed_at_utc, $completedAtUtc) ELSE NULL END,
                    updated_at_utc = $updatedAtUtc
                WHERE job_id = $jobId;
                """;
            command.Parameters.AddWithValue("$jobId", jobId.ToString("D"));
            command.Parameters.AddWithValue("$stage", stage);
            command.Parameters.AddWithValue("$progressPercent", Math.Clamp(progressPercent, 0, 100));
            command.Parameters.AddWithValue("$error", error is null ? DBNull.Value : error);
            command.Parameters.AddWithValue("$clearPaths", clearPaths ? 1 : 0);
            command.Parameters.AddWithValue("$cancelRequested", cancelRequested.HasValue ? (cancelRequested.Value ? 1 : 0) : DBNull.Value);
            command.Parameters.AddWithValue("$completedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$updatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }

    private static DownloadJobResponse Map(SqliteDataReader reader)
    {
        return new DownloadJobResponse
        {
            JobId = Guid.Parse(reader.GetString(0)),
            Source = reader.GetString(1),
            SourceId = reader.GetString(2),
            DisplayName = reader.GetString(3),
            SourceUrl = reader.GetString(4),
            Stage = reader.GetString(5),
            ProgressPercent = reader.GetDouble(6),
            DownloadedPath = reader.IsDBNull(7) ? null : reader.GetString(7),
            StagedPath = reader.IsDBNull(8) ? null : reader.GetString(8),
            InstalledPath = reader.IsDBNull(9) ? null : reader.GetString(9),
            Error = reader.IsDBNull(10) ? null : reader.GetString(10),
            CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(11)),
            UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(12)),
        };
    }
}
