using System.ComponentModel.DataAnnotations;

namespace ChartHub.Server.Contracts;

public sealed class CreateDownloadJobRequest
{
    public required string Source { get; init; }

    public required string SourceId { get; init; }

    [MaxLength(500)]
    public required string DisplayName { get; init; }

    [MaxLength(2048)]
    public required string SourceUrl { get; init; }
}

public sealed class DownloadJobResponse
{
    public required Guid JobId { get; init; }

    public required string Source { get; init; }

    public required string SourceId { get; init; }

    public required string DisplayName { get; init; }

    public required string SourceUrl { get; init; }

    public required string Stage { get; init; }

    public required double ProgressPercent { get; init; }

    public string? DownloadedPath { get; init; }

    public string? StagedPath { get; init; }

    public string? InstalledPath { get; init; }

    public string? InstalledRelativePath { get; init; }

    public string? Artist { get; init; }

    public string? Title { get; init; }

    public string? Charter { get; init; }

    public string? SourceMd5 { get; init; }

    public string? SourceChartHash { get; init; }

    public string? Error { get; init; }

    public string? FileType { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class DownloadProgressEvent
{
    public required Guid JobId { get; init; }

    public required string Stage { get; init; }

    public required double ProgressPercent { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class JobLogEntryResponse
{
    public required DateTimeOffset TimestampUtc { get; init; }

    public required string Level { get; init; }

    public required int EventId { get; init; }

    public string? Category { get; init; }

    public required string Message { get; init; }

    public string? Exception { get; init; }
}
