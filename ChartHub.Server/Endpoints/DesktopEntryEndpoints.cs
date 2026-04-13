using System.Text.Json;

using ChartHub.Server.Contracts;
using ChartHub.Server.Services;

namespace ChartHub.Server.Endpoints;

public static class DesktopEntryEndpoints
{
    public static RouteGroupBuilder MapDesktopEntryEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/desktopentries")
            .WithTags("DesktopEntry")
            .RequireAuthorization();

        group.MapGet(string.Empty, ListDesktopEntriesAsync)
            .WithName("ListDesktopEntries")
            .WithSummary("List available desktop entries and runtime status");

        group.MapPost("/{entryId}/execute", ExecuteDesktopEntryAsync)
            .WithName("ExecuteDesktopEntry")
            .WithSummary("Execute a desktop entry")
            .Produces<DesktopEntryActionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status501NotImplemented);

        group.MapPost("/{entryId}/kill", KillDesktopEntryAsync)
            .WithName("KillDesktopEntry")
            .WithSummary("Send SIGTERM to a tracked desktop-entry process")
            .Produces<DesktopEntryActionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status501NotImplemented);

        group.MapPost("/refresh", RefreshDesktopEntryCatalogAsync)
            .WithName("RefreshDesktopEntryCatalog")
            .WithSummary("Refresh desktop-entry catalog and icon cache")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status501NotImplemented);

        group.MapGet("/stream", StreamDesktopEntriesAsync)
            .WithName("StreamDesktopEntries")
            .WithSummary("Stream desktop-entry snapshots over SSE")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet("/desktopentry-icons/{entryId}/{fileName}", GetDesktopEntryIconAsync)
            .WithName("GetDesktopEntryIcon")
            .WithTags("DesktopEntry")
            .WithSummary("Get cached desktop-entry icon")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> ListDesktopEntriesAsync(IDesktopEntryService service, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<DesktopEntryItemResponse> items = await service.ListEntriesAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(items);
        }
        catch (DesktopEntryServiceException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static async Task<IResult> ExecuteDesktopEntryAsync(
        string entryId,
        IDesktopEntryService service,
        CancellationToken cancellationToken)
    {
        try
        {
            DesktopEntryActionResponse result = await service.ExecuteAsync(entryId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (DesktopEntryServiceException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static async Task<IResult> KillDesktopEntryAsync(
        string entryId,
        IDesktopEntryService service,
        CancellationToken cancellationToken)
    {
        try
        {
            DesktopEntryActionResponse result = await service.KillAsync(entryId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (DesktopEntryServiceException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static async Task<IResult> RefreshDesktopEntryCatalogAsync(
        IDesktopEntryService service,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.RefreshCatalogAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok();
        }
        catch (DesktopEntryServiceException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static async Task<IResult> StreamDesktopEntriesAsync(
        HttpContext context,
        IDesktopEntryService service,
        CancellationToken cancellationToken)
    {
        try
        {
            context.Response.Headers.Append("Content-Type", "text/event-stream");
            context.Response.Headers.Append("Cache-Control", "no-cache");

            while (!cancellationToken.IsCancellationRequested)
            {
                IReadOnlyList<DesktopEntryItemResponse> items = await service.ListEntriesAsync(cancellationToken).ConfigureAwait(false);
                DesktopEntrySnapshotEventResponse payload = new()
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Items = items,
                };

                await context.Response.WriteAsync("event: desktopentries\n", cancellationToken).ConfigureAwait(false);
                await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", cancellationToken).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, service.SseIntervalSeconds)), cancellationToken).ConfigureAwait(false);
            }

            return Results.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Results.Empty;
        }
        catch (DesktopEntryServiceException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static IResult GetDesktopEntryIconAsync(
        string entryId,
        string fileName,
        IDesktopEntryService service)
    {
        if (!service.TryResolveIconFile(entryId, fileName, out string iconPath, out string contentType))
        {
            return Results.NotFound();
        }

        return Results.File(iconPath, contentType);
    }

    private static IResult MapServiceException(DesktopEntryServiceException ex)
    {
        if (ex.StatusCode == StatusCodes.Status409Conflict)
        {
            return Results.Conflict(new { error = ex.ErrorCode, message = ex.Message });
        }

        if (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            return Results.NotFound(new { error = ex.ErrorCode, message = ex.Message });
        }

        if (ex.StatusCode == StatusCodes.Status501NotImplemented)
        {
            return Results.StatusCode(StatusCodes.Status501NotImplemented);
        }

        if (ex.StatusCode == StatusCodes.Status400BadRequest)
        {
            return Results.BadRequest(new { error = ex.ErrorCode, message = ex.Message });
        }

        return Results.Problem(statusCode: ex.StatusCode, title: ex.ErrorCode, detail: ex.Message);
    }
}
