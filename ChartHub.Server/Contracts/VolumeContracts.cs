using System.ComponentModel.DataAnnotations;

namespace ChartHub.Server.Contracts;

public sealed class VolumeStateResponse
{
    public DateTimeOffset UpdatedAtUtc { get; init; }

    public required VolumeMasterStateResponse Master { get; init; }

    public IReadOnlyList<VolumeSessionResponse> Sessions { get; init; } = [];

    public bool SupportsPerApplicationSessions { get; init; }

    public string? SessionSupportMessage { get; init; }
}

public sealed class VolumeMasterStateResponse
{
    public int ValuePercent { get; init; }

    public bool IsMuted { get; init; }
}

public sealed class VolumeSessionResponse
{
    public required string SessionId { get; init; }

    public required string Name { get; init; }

    public int? ProcessId { get; init; }

    public string? ApplicationName { get; init; }

    public int ValuePercent { get; init; }

    public bool IsMuted { get; init; }
}

public sealed class VolumeActionResponse
{
    public required string TargetId { get; init; }

    public required string TargetKind { get; init; }

    public required string Name { get; init; }

    public int ValuePercent { get; init; }

    public bool IsMuted { get; init; }

    public required string Message { get; init; }
}

public sealed class VolumeSnapshotEventResponse
{
    public DateTimeOffset UpdatedAtUtc { get; init; }

    public required VolumeStateResponse State { get; init; }
}

public sealed class SetVolumeRequest
{
    [Range(0, 100)]
    public int ValuePercent { get; init; }
}