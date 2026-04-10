using System.Text.Json;

using ChartHub.Server.Contracts;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Mvc;

namespace ChartHub.Server.Endpoints;

public static class DownloadEndpoints
{
    public static RouteGroupBuilder MapDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/downloads")
            .WithTags("Downloads")
            .RequireAuthorization();

        group.MapPost("/jobs", ([FromBody] CreateDownloadJobRequest request, IDownloadJobStore store) =>
            {
                DownloadJobResponse created = store.Create(request);
                return Results.Created($"/api/v1/downloads/jobs/{created.JobId}", created);
            })
            .WithName("CreateDownloadJob")
            .WithSummary("Create a new download job");

        group.MapGet("/jobs", (IDownloadJobStore store) => Results.Ok(store.List()))
            .WithName("ListDownloadJobs")
            .WithSummary("List current download jobs");

        group.MapGet("/jobs/{jobId:guid}", (Guid jobId, IDownloadJobStore store) =>
            store.TryGet(jobId, out DownloadJobResponse? job)
                ? Results.Ok(job)
                : Results.NotFound())
            .WithName("GetDownloadJob")
            .WithSummary("Get a specific download job");

        group.MapPost("/jobs/{jobId:guid}/retry", (Guid jobId, IDownloadJobStore store) =>
            {
                if (!store.TryGet(jobId, out _))
                {
                    return Results.NotFound();
                }

                store.QueueRetry(jobId);
                return Results.Accepted($"/api/v1/downloads/jobs/{jobId}");
            })
            .WithName("RetryDownloadJob")
            .WithSummary("Retry a failed job");

        group.MapPost("/jobs/{jobId:guid}/cancel", (Guid jobId, IDownloadJobStore store) =>
            {
                if (!store.TryGet(jobId, out _))
                {
                    return Results.NotFound();
                }

                store.RequestCancel(jobId);
                return Results.Accepted($"/api/v1/downloads/jobs/{jobId}");
            })
            .WithName("CancelDownloadJob")
            .WithSummary("Cancel an active job");

        group.MapGet("/jobs/{jobId:guid}/stream", async (
                HttpContext context,
                Guid jobId,
                IDownloadJobStore store,
                CancellationToken cancellationToken) =>
            {
                if (!store.TryGet(jobId, out _))
                {
                    return Results.NotFound();
                }

                context.Response.Headers.Append("Content-Type", "text/event-stream");
                context.Response.Headers.Append("Cache-Control", "no-cache");

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!store.TryGet(jobId, out DownloadJobResponse? job))
                    {
                        return Results.NotFound();
                    }

                    if (job is null)
                    {
                        return Results.NotFound();
                    }

                    DownloadProgressEvent payload = new()
                    {
                        JobId = job.JobId,
                        Stage = job.Stage,
                        ProgressPercent = job.ProgressPercent,
                        UpdatedAtUtc = job.UpdatedAtUtc,
                    };

                    await context.Response.WriteAsync($"event: job\n", cancellationToken).ConfigureAwait(false);
                    await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", cancellationToken)
                        .ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }

                return Results.Empty;
            })
            .WithName("StreamDownloadJob")
            .WithSummary("Stream a single job progress over SSE");

        group.MapGet("/jobs/stream", async (HttpContext context, IDownloadJobStore store, CancellationToken cancellationToken) =>
            {
                context.Response.Headers.Append("Content-Type", "text/event-stream");
                context.Response.Headers.Append("Cache-Control", "no-cache");

                while (!cancellationToken.IsCancellationRequested)
                {
                    IReadOnlyList<DownloadJobResponse> jobs = store.List();
                    IEnumerable<DownloadProgressEvent> eventsPayload = jobs.Select(job => new DownloadProgressEvent
                    {
                        JobId = job.JobId,
                        Stage = job.Stage,
                        ProgressPercent = job.ProgressPercent,
                        UpdatedAtUtc = job.UpdatedAtUtc,
                    });

                    await context.Response.WriteAsync($"event: jobs\n", cancellationToken).ConfigureAwait(false);
                    await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(eventsPayload)}\n\n", cancellationToken)
                        .ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
            })
            .WithName("StreamDownloadJobs")
            .WithSummary("Stream download progress events over SSE");

        return group;
    }
}
