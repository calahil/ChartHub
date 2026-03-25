using System.Text.Json.Nodes;

using ChartHub.BackupApi.Options;
using ChartHub.BackupApi.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ChartHub.BackupApi.Endpoints;

public static class RhythmVerseEndpoints
{
    public static IEndpointRouteBuilder MapRhythmVerseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/rhythmverse");

        group.MapGet("/songs", GetSongsAsync);
        group.MapGet("/songs/{songId:long}", GetSongByIdAsync);
        group.MapGet("/download/{fileId}", RedirectDownloadAsync);
        group.MapGet("/health/sync", GetSyncHealthAsync);

        endpoints.MapGet("/api/schemas/rhythmverse-song-list.json", GetJsonSchema);
        endpoints.MapGet("/api/schemas/rhythmverse-song-list.openapi.json", GetOpenApiSchema);

        return endpoints;
    }

    private static async Task<IResult> GetSongsAsync(
        [FromServices] IRhythmVerseRepository repository,
        [FromQuery] int page = 1,
        [FromQuery] int records = 25,
        [FromQuery(Name = "q")] string? query = null,
        [FromQuery] string? genre = null,
        [FromQuery] string? gameformat = null,
        [FromQuery] string? author = null,
        [FromQuery] string? group = null,
        CancellationToken cancellationToken = default)
    {
        Models.RhythmVersePageEnvelope envelope = await repository
            .GetSongsPageAsync(page, records, query, genre, gameformat, author, group, cancellationToken)
            .ConfigureAwait(false);

        JsonObject payload = new()
        {
            ["status"] = "success",
            ["data"] = new JsonObject
            {
                ["records"] = new JsonObject
                {
                    ["total_available"] = envelope.TotalAvailable,
                    ["total_filtered"] = envelope.TotalFiltered,
                    ["returned"] = envelope.Returned,
                },
                ["pagination"] = new JsonObject
                {
                    ["start"] = envelope.Start,
                    ["records"] = envelope.Records,
                    ["page"] = envelope.Page,
                },
                ["songs"] = new JsonArray(envelope.Songs.ToArray()),
            },
        };

        return Results.Json(payload);
    }

    private static async Task<IResult> RedirectDownloadAsync(
        [FromServices] IRhythmVerseRepository repository,
        [FromServices] IOptions<DownloadOptions> options,
        string fileId,
        CancellationToken cancellationToken)
    {
        string mode = options.Value.Mode;
        if (!string.Equals(mode, "redirect", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = "Only redirect mode is currently implemented." });
        }

        string? url = await repository.GetDownloadUrlByFileIdAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(url))
        {
            return Results.NotFound();
        }

        return Results.Redirect(url);
    }

    private static IResult GetJsonSchema([FromServices] ISchemaDocumentService schemaDocumentService)
    {
        return Results.Text(schemaDocumentService.BuildJsonSchema(), "application/schema+json");
    }

    private static IResult GetOpenApiSchema([FromServices] ISchemaDocumentService schemaDocumentService)
    {
        return Results.Text(schemaDocumentService.BuildOpenApiComponentsSchema(), "application/json");
    }

    private static async Task<IResult> GetSongByIdAsync(
        [FromServices] IRhythmVerseRepository repository,
        long songId,
        CancellationToken cancellationToken)
    {
        JsonNode? song = await repository.GetSongByIdAsync(songId, cancellationToken).ConfigureAwait(false);
        return song is null ? Results.NotFound() : Results.Json(song);
    }

    private static async Task<IResult> GetSyncHealthAsync(
        [FromServices] IRhythmVerseRepository repository,
        CancellationToken cancellationToken)
    {
        string? lastSuccessUtc = await repository.GetSyncStateAsync("sync.last_success_utc", cancellationToken).ConfigureAwait(false);
        string? totalAvailable = await repository.GetSyncStateAsync("records.total_available", cancellationToken).ConfigureAwait(false);
        string? lastRecordUpdated = await repository.GetSyncStateAsync("sync.last_record_updated", cancellationToken).ConfigureAwait(false);

        long? lagSeconds = null;
        if (lastSuccessUtc is not null
            && DateTimeOffset.TryParse(lastSuccessUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset parsedSuccess))
        {
            lagSeconds = (long)(DateTimeOffset.UtcNow - parsedSuccess).TotalSeconds;
        }

        return Results.Ok(new
        {
            last_success_utc = lastSuccessUtc,
            lag_seconds = lagSeconds,
            total_available = totalAvailable,
            last_record_updated_unix = lastRecordUpdated,
        });
    }
}
