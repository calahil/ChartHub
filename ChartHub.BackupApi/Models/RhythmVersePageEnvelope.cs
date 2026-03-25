using System.Text.Json.Nodes;

namespace ChartHub.BackupApi.Models;

public sealed class RhythmVersePageEnvelope
{
    public required int TotalAvailable { get; init; }

    public required int TotalFiltered { get; init; }

    public required int Returned { get; init; }

    public required int Start { get; init; }

    public required int Records { get; init; }

    public required int Page { get; init; }

    public required IReadOnlyList<JsonNode?> Songs { get; init; }
}
