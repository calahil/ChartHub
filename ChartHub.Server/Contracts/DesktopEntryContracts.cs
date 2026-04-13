namespace ChartHub.Server.Contracts;

public sealed class DesktopEntryItemResponse
{
    public required string EntryId { get; init; }

    public required string Name { get; init; }

    public required string Status { get; init; }

    public int? ProcessId { get; init; }

    public string? IconUrl { get; init; }
}

public sealed class DesktopEntryActionResponse
{
    public required string EntryId { get; init; }

    public required string Status { get; init; }

    public int? ProcessId { get; init; }

    public required string Message { get; init; }
}

public sealed class DesktopEntrySnapshotEventResponse
{
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public required IReadOnlyList<DesktopEntryItemResponse> Items { get; init; }
}
