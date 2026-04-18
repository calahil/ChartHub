namespace ChartHub.Server.Contracts;

public sealed class TranscriptionJobSummaryResponse
{
    public string JobId { get; init; } = "";
    public string SongId { get; init; } = "";
    public string Aggressiveness { get; init; } = "";
    public string Status { get; init; } = "";
    public string? ClaimedByRunnerId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ClaimedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string? FailureReason { get; init; }
    public int AttemptNumber { get; init; }
}

public sealed class TranscriptionResultResponse
{
    public string ResultId { get; init; } = "";
    public string JobId { get; init; } = "";
    public string SongId { get; init; } = "";
    public string Aggressiveness { get; init; } = "";
    public string MidiFilePath { get; init; } = "";
    public DateTimeOffset CompletedAtUtc { get; init; }
    public bool IsApproved { get; init; }
    public DateTimeOffset? ApprovedAtUtc { get; init; }
}

public sealed class RetryTranscriptionRequest
{
    /// <summary>One of: Low, Medium, High.</summary>
    public string Aggressiveness { get; init; } = "Medium";
}
