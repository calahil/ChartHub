using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc;

namespace ChartHub.BackupApi.Models;

public sealed class BackupApiHealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "ok";
}

public sealed class BackupApiErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;
}

public sealed class CompatibilitySongsFormRequest
{
    [FromForm(Name = "page")]
    [JsonPropertyName("page")]
    public int? Page { get; init; }

    [FromForm(Name = "records")]
    [JsonPropertyName("records")]
    public int? Records { get; init; }

    [FromForm(Name = "author")]
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [FromForm(Name = "instrument")]
    [JsonPropertyName("instrument")]
    public string[]? Instrument { get; init; }

    [FromForm(Name = "sort[0][sort_by]")]
    [JsonPropertyName("sort[0][sort_by]")]
    public string? SortBy { get; init; }

    [FromForm(Name = "sort[0][sort_order]")]
    [JsonPropertyName("sort[0][sort_order]")]
    public string? SortOrder { get; init; }

    [FromForm(Name = "text")]
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [FromForm(Name = "data_type")]
    [JsonPropertyName("data_type")]
    public string? DataType { get; init; }
}

public sealed class CompatibilitySongsResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "success";

    [JsonPropertyName("data")]
    public CompatibilitySongsData Data { get; init; } = new();
}

public sealed class CompatibilitySongsData
{
    [JsonPropertyName("records")]
    public CompatibilitySongsRecords Records { get; init; } = new();

    [JsonPropertyName("pagination")]
    public CompatibilitySongsPagination Pagination { get; init; } = new();

    [JsonPropertyName("songs")]
    public JsonNode[] Songs { get; init; } = [];
}

public sealed class CompatibilitySongsRecords
{
    [JsonPropertyName("total_available")]
    public int TotalAvailable { get; init; }

    [JsonPropertyName("total_filtered")]
    public int TotalFiltered { get; init; }

    [JsonPropertyName("returned")]
    public int Returned { get; init; }
}

public sealed class CompatibilitySongsPagination
{
    [JsonPropertyName("start")]
    public int Start { get; init; }

    [JsonPropertyName("records")]
    public int Records { get; init; }

    [JsonPropertyName("page")]
    public int Page { get; init; }
}

public sealed class SyncHealthResponse
{
    [JsonPropertyName("last_success_utc")]
    public string? LastSuccessUtc { get; init; }

    [JsonPropertyName("lag_seconds")]
    public long? LagSeconds { get; init; }

    [JsonPropertyName("total_available")]
    public string? TotalAvailable { get; init; }

    [JsonPropertyName("last_record_updated_unix")]
    public string? LastRecordUpdatedUnix { get; init; }

    [JsonPropertyName("reconciliation_current_run_id")]
    public string? ReconciliationCurrentRunId { get; init; }

    [JsonPropertyName("reconciliation_started_utc")]
    public string? ReconciliationStartedUtc { get; init; }

    [JsonPropertyName("reconciliation_completed_utc")]
    public string? ReconciliationCompletedUtc { get; init; }

    [JsonPropertyName("reconciliation_in_progress")]
    public bool ReconciliationInProgress { get; init; }

    [JsonPropertyName("last_run_completed")]
    public bool LastRunCompleted { get; init; }
}
