using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using ChartHub.Server.Contracts;
using ChartHub.Server.Services;

namespace ChartHub.Server.Endpoints;

public static class VolumeEndpoints
{
    public static RouteGroupBuilder MapVolumeEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/volume")
            .WithTags("Volume")
            .RequireAuthorization();

        group.MapGet(string.Empty, GetVolumeStateAsync)
            .WithName("GetVolumeState")
            .WithSummary("Get current master volume and per-application session volumes")
            .Produces<VolumeStateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status501NotImplemented);

        group.MapPost("/master", SetMasterVolumeAsync)
            .WithName("SetMasterVolume")
            .WithSummary("Set the server master volume")
            .Produces<VolumeActionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status501NotImplemented);

        group.MapPost("/sessions/{sessionId}", SetSessionVolumeAsync)
            .WithName("SetSessionVolume")
            .WithSummary("Set the volume of a running per-application audio session")
            .Produces<VolumeActionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status501NotImplemented);

        group.MapGet("/stream", StreamVolumeAsync)
            .WithName("StreamVolume")
            .WithSummary("Stream volume snapshots over SSE")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status501NotImplemented);

        return group;
    }

    private static async Task<IResult> GetVolumeStateAsync(IVolumeService service, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.GetStateAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (VolumeServiceException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static async Task<IResult> SetMasterVolumeAsync(
        SetVolumeRequest request,
        IVolumeService service,
        CancellationToken cancellationToken)
    {
        ValidationContext context = new(request);
        Validator.ValidateObject(request, context, validateAllProperties: true);

        try
        {
            return Results.Ok(await service.SetMasterVolumeAsync(request.ValuePercent, cancellationToken).ConfigureAwait(false));
        }
        catch (VolumeServiceException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static async Task<IResult> SetSessionVolumeAsync(
        string sessionId,
        SetVolumeRequest request,
        IVolumeService service,
        CancellationToken cancellationToken)
    {
        ValidationContext context = new(request);
        Validator.ValidateObject(request, context, validateAllProperties: true);

        try
        {
            return Results.Ok(await service.SetSessionVolumeAsync(sessionId, request.ValuePercent, cancellationToken).ConfigureAwait(false));
        }
        catch (VolumeServiceException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static async Task<IResult> StreamVolumeAsync(
        HttpContext context,
        IVolumeService service,
        CancellationToken cancellationToken)
    {
        try
        {
            context.Response.Headers.Append("Content-Type", "text/event-stream");
            context.Response.Headers.Append("Cache-Control", "no-cache");

            long observedChangeStamp = service.CurrentChangeStamp;

            while (!cancellationToken.IsCancellationRequested)
            {
                VolumeStateResponse state = await service.GetStateAsync(cancellationToken).ConfigureAwait(false);
                VolumeSnapshotEventResponse payload = new()
                {
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    State = state,
                };

                await context.Response.WriteAsync("event: volume\n", cancellationToken).ConfigureAwait(false);
                await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", cancellationToken).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

                observedChangeStamp = service.CurrentChangeStamp;
                await service.WaitForChangeAsync(
                        observedChangeStamp,
                        TimeSpan.FromSeconds(Math.Max(1, service.SseHeartbeatSeconds)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return Results.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Results.Empty;
        }
        catch (VolumeServiceException ex)
        {
            return MapServiceException(ex);
        }
    }

    private static IResult MapServiceException(VolumeServiceException ex)
    {
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