using ChartHub.Services;

namespace ChartHub.Models;

public sealed class LocalIngestionEntry
{
    public string LocalPath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Source { get; init; } = LibrarySourceNames.RhythmVerse;
    public string? SourceId { get; init; }
    public string? SourceLink { get; init; }
    public string? Artist { get; init; }
    public string? Title { get; init; }
    public string? Charter { get; init; }
    public string? LibrarySource { get; init; }
}