namespace ChartHub.Server.Services;

public sealed record JobLogEntry(
    DateTimeOffset TimestampUtc,
    string Level,
    int EventId,
    string? Category,
    string Message,
    string? Exception);

public interface IJobLogSink
{
    void Add(Guid jobId, LogLevel level, EventId eventId, string? category, string message, string? exception);

    IReadOnlyList<JobLogEntry> GetLogs(Guid jobId);
}
