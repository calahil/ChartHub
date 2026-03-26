using System.Text.Json.Nodes;

using ChartHub.BackupApi.Models;
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

        group.MapGet("/songs/{songId:long}", GetSongByIdAsync)
            .WithName("GetRhythmVerseSongById")
            .WithTags("RhythmVerse")
            .WithSummary("Get a mirrored RhythmVerse song by ID")
            .WithDescription("Returns the stored upstream song payload for the requested song ID. Soft-deleted songs are excluded.")
            .Produces<JsonNode>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/download/{fileId}", RedirectDownloadAsync)
            .WithName("RedirectRhythmVerseDownload")
            .WithTags("RhythmVerse")
            .WithSummary("Resolve a mirrored file ID to its download URL")
            .WithDescription("Redirects to the stored upstream download URL for a mirrored file. Only redirect mode is currently supported.")
            .Produces(StatusCodes.Status302Found)
            .Produces<BackupApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/health/sync", GetSyncHealthAsync)
            .WithName("GetRhythmVerseSyncHealth")
            .WithTags("RhythmVerse")
            .WithSummary("Get RhythmVerse synchronization health")
            .WithDescription("Returns mirror synchronization metadata, reconciliation state, and lag indicators for operational monitoring.")
            .Produces<SyncHealthResponse>(StatusCodes.Status200OK);

        endpoints.MapPost("/api/all/songfiles/list", GetSongsListCompatAsync)
            .WithName("ListRhythmVerseSongsCompat")
            .WithTags("RhythmVerse Compatibility")
            .WithSummary("Compatibility list endpoint for RhythmVerse clients")
            .WithDescription("Mirrors upstream form contract for listing songs. Form-data keys: `page` (default 1), `records` (default 25, clamped 1-250), `author` (author_id or shortname match), repeatable `instrument` (OR semantics), `sort[0][sort_by]`, `sort[0][sort_order]`, optional legacy `data_type` (accepted, currently ignored).")
            .Accepts<CompatibilitySongsFormRequest>("application/x-www-form-urlencoded")
            .Produces<CompatibilitySongsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapPost("/api/all/songfiles/search/live", GetSongsSearchCompatAsync)
            .WithName("SearchRhythmVerseSongsCompat")
            .WithTags("RhythmVerse Compatibility")
            .WithSummary("Compatibility search endpoint for RhythmVerse clients")
            .WithDescription("Mirrors upstream search form contract and applies free-text filtering over mirrored artist/title/album fields. Same keys as list endpoint plus `text` search term (empty/whitespace behaves like list mode).")
            .Accepts<CompatibilitySongsFormRequest>("application/x-www-form-urlencoded")
            .Produces<CompatibilitySongsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        endpoints.MapGet("/api/schemas/rhythmverse-song-list.json", GetJsonSchema)
            .WithName("GetRhythmVerseSongListJsonSchema")
            .WithTags("Schemas")
            .WithSummary("Get JSON Schema for compatibility song-list envelope")
            .WithDescription("Returns a JSON Schema document that describes the compatibility song-list response envelope.")
            .Produces(StatusCodes.Status200OK, contentType: "application/schema+json");

        endpoints.MapGet("/api/schemas/rhythmverse-song-list.openapi.json", GetOpenApiSchema)
            .WithName("GetRhythmVerseSongListOpenApiSchema")
            .WithTags("Schemas")
            .WithSummary("Get OpenAPI components schema for compatibility song-list envelope")
            .WithDescription("Returns OpenAPI-compatible schema components for the compatibility response contract.")
            .Produces(StatusCodes.Status200OK, contentType: "application/json");

        endpoints.MapGet("/img/{**path}", GetMirroredImageAsync)
            .WithName("GetMirroredImage")
            .WithTags("Assets")
            .WithSummary("Get a mirrored RhythmVerse image asset")
            .WithDescription("Fetches an upstream RhythmVerse image asset on cache miss and serves the cached bytes on subsequent requests.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/avatars/{**path}", GetMirroredAvatarAsync)
            .WithName("GetMirroredAvatar")
            .WithTags("Assets")
            .WithSummary("Get a mirrored RhythmVerse avatar asset")
            .WithDescription("Fetches an upstream RhythmVerse avatar asset on cache miss and serves the cached bytes on subsequent requests.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/assets/album_art/{**path}", GetMirroredAlbumArtAsync)
            .WithName("GetMirroredAlbumArt")
            .WithTags("Assets")
            .WithSummary("Get mirrored RhythmVerse album art")
            .WithDescription("Fetches upstream RhythmVerse album-art assets on cache miss and serves the cached bytes on subsequent requests.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/download_file/{**path}", GetMirroredDownloadFileAsync)
            .WithName("GetMirroredDownloadFile")
            .WithTags("Assets")
            .WithSummary("Get a mirrored RhythmVerse download file")
            .WithDescription("Fetches upstream RhythmVerse download files on cache miss and serves cached files on subsequent requests.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        endpoints.MapGet("/downloads/external", GetMirroredExternalDownloadAsync)
            .WithName("GetMirroredExternalDownload")
            .WithTags("Assets")
            .WithSummary("Get a mirrored external download")
            .WithDescription("Fetches supported external downloads on cache miss, caches resolved redirects and file bytes on disk, and serves cached files on subsequent requests.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static Task<IResult> GetMirroredImageAsync(
        [FromServices] IImageProxyService imageProxyService,
        string path,
        CancellationToken cancellationToken)
    {
        return GetMirroredAssetAsync(imageProxyService, $"img/{path}", cancellationToken);
    }

    private static Task<IResult> GetMirroredAvatarAsync(
        [FromServices] IImageProxyService imageProxyService,
        string path,
        CancellationToken cancellationToken)
    {
        return GetMirroredAssetAsync(imageProxyService, $"avatars/{path}", cancellationToken);
    }

    private static Task<IResult> GetMirroredAlbumArtAsync(
        [FromServices] IImageProxyService imageProxyService,
        string path,
        CancellationToken cancellationToken)
    {
        return GetMirroredAssetAsync(imageProxyService, $"assets/album_art/{path}", cancellationToken);
    }

    private static async Task<IResult> GetMirroredDownloadFileAsync(
        [FromServices] IDownloadProxyService downloadProxyService,
        string path,
        CancellationToken cancellationToken)
    {
        DownloadProxyResult? result = await downloadProxyService
            .GetDownloadFileAsync($"download_file/{path}", cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? Results.NotFound()
            : Results.File(result.FilePath, result.ContentType, enableRangeProcessing: true);
    }

    private static async Task<IResult> GetMirroredExternalDownloadAsync(
        [FromServices] IDownloadProxyService downloadProxyService,
        [FromQuery] string sourceUrl,
        CancellationToken cancellationToken)
    {
        DownloadProxyResult? result = await downloadProxyService
            .GetExternalDownloadAsync(sourceUrl, cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? Results.NotFound()
            : Results.File(result.FilePath, result.ContentType, enableRangeProcessing: true);
    }

    private static Task<IResult> GetSongsListCompatAsync(
        HttpRequest request,
        [FromServices] IRhythmVerseRepository repository,
        CancellationToken cancellationToken = default)
    {
        return GetSongsFromCompatFormAsync(request, repository, includeSearchText: false, cancellationToken);
    }

    private static Task<IResult> GetSongsSearchCompatAsync(
        HttpRequest request,
        [FromServices] IRhythmVerseRepository repository,
        CancellationToken cancellationToken = default)
    {
        return GetSongsFromCompatFormAsync(request, repository, includeSearchText: true, cancellationToken);
    }

    private static async Task<IResult> GetSongsFromCompatFormAsync(
        HttpRequest request,
        [FromServices] IRhythmVerseRepository repository,
        bool includeSearchText,
        CancellationToken cancellationToken = default)
    {
        IFormCollection form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);

        int page = ParseInt(form["page"], 1);
        int records = ParseInt(form["records"], 25);
        string? query = includeSearchText ? Normalize(form["text"]) : null;
        string? author = Normalize(form["author"]);
        string? sortBy = Normalize(form["sort[0][sort_by]"]);
        string? sortOrder = Normalize(form["sort[0][sort_order]"]);
        var instruments = form["instrument"]
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToList();

        Models.RhythmVersePageEnvelope envelope = await repository
            .GetSongsPageAdvancedAsync(
                page,
                records,
                query,
                genre: null,
                gameformat: null,
                author,
                group: null,
                sortBy,
                sortOrder,
                instruments,
                cancellationToken)
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

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int ParseInt(string? value, int fallback)
    {
        if (int.TryParse(value, out int parsed))
        {
            return parsed;
        }

        return fallback;
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
        string? reconciliationCurrentRunId = await repository.GetSyncStateAsync("reconciliation.current_run_id", cancellationToken).ConfigureAwait(false);
        string? reconciliationStartedUtc = await repository.GetSyncStateAsync("reconciliation.started_utc", cancellationToken).ConfigureAwait(false);
        string? reconciliationCompletedUtc = await repository.GetSyncStateAsync("reconciliation.completed_utc", cancellationToken).ConfigureAwait(false);

        long? lagSeconds = null;
        if (lastSuccessUtc is not null
            && DateTimeOffset.TryParse(lastSuccessUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset parsedSuccess))
        {
            lagSeconds = (long)(DateTimeOffset.UtcNow - parsedSuccess).TotalSeconds;
        }

        bool reconciliationInProgress = reconciliationStartedUtc is not null;

        if (reconciliationInProgress
            && reconciliationCompletedUtc is not null
            && DateTimeOffset.TryParse(reconciliationStartedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset parsedReconciliationStart)
            && DateTimeOffset.TryParse(reconciliationCompletedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset parsedReconciliationCompletion)
            && parsedReconciliationCompletion >= parsedReconciliationStart)
        {
            reconciliationInProgress = false;
        }

        bool lastRunCompleted = !reconciliationInProgress && reconciliationCompletedUtc is not null;

        return Results.Ok(new
        {
            last_success_utc = lastSuccessUtc,
            lag_seconds = lagSeconds,
            total_available = totalAvailable,
            last_record_updated_unix = lastRecordUpdated,
            reconciliation_current_run_id = reconciliationCurrentRunId,
            reconciliation_started_utc = reconciliationStartedUtc,
            reconciliation_completed_utc = reconciliationCompletedUtc,
            reconciliation_in_progress = reconciliationInProgress,
            last_run_completed = lastRunCompleted,
        });
    }

    private static async Task<IResult> GetMirroredAssetAsync(
        IImageProxyService imageProxyService,
        string assetPath,
        CancellationToken cancellationToken)
    {
        ImageProxyResult? result = await imageProxyService.GetImageAsync(assetPath, cancellationToken).ConfigureAwait(false);
        return result is null
            ? Results.NotFound()
            : Results.File(result.Data, result.ContentType);
    }
}
