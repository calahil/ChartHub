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

    /// <summary>
    /// When true the server will auto-queue an AI drum transcription job after install
    /// for songs whose source has no drum track.
    /// </summary>
    public bool DrumGenRequested { get; init; }
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

    /// <summary>
    /// Non-fatal conversion statuses recorded during install (for example audio fallback).
    /// </summary>
    public IReadOnlyList<DownloadJobStatus> ConversionStatuses { get; init; } = [];

    /// <summary>Whether AI drum generation was requested for this job.</summary>
    public bool DrumGenRequested { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class DownloadJobStatus
{
    /// <summary>
    /// Stable machine-readable status code. Known values include:
    /// - audio-incomplete: only backing audio was produced.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>Human-readable detail for the status code.</summary>
    public required string Message { get; init; }
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
