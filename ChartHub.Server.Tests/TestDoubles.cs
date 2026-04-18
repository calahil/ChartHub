using ChartHub.Server.Services;

namespace ChartHub.Server.Tests;

/// <summary>Shared null/stub implementations used across multiple integration test fixtures.</summary>
internal sealed class NullTranscriptionJobStore : ITranscriptionJobStore
{
    public TranscriptionJob CreateJob(string songId, string songFolderPath, TranscriptionAggressiveness aggressiveness, int attemptNumber = 1) =>
        new(Guid.NewGuid().ToString(), songId, songFolderPath, aggressiveness, TranscriptionJobStatus.Queued, null, DateTimeOffset.UtcNow, null, null, null, attemptNumber);

    public TranscriptionJob? TryClaimNext(string runnerId) => null;

    public void UpdateStatus(string jobId, TranscriptionJobStatus status, string? failureReason = null) { }

    public void MarkCompleted(string jobId, string midiFilePath) { }

    public IReadOnlyList<TranscriptionJob> ListJobs(string? songId = null, TranscriptionJobStatus? status = null) => [];

    public TranscriptionJob? GetJob(string jobId) => null;

    public bool DeleteJob(string jobId) => false;

    public TranscriptionResult? GetLatestApprovedResult(string songId) => null;

    public IReadOnlyList<TranscriptionResult> ListResults(string? songId = null) => [];

    public void ApproveResult(string resultId) { }
}
