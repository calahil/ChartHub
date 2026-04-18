using System.Text.Json;

using ChartHub.Server.Contracts;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Mvc;

namespace ChartHub.Server.Endpoints;

public static partial class DownloadEndpoints
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

        group.MapDelete("/jobs/{jobId:guid}", (Guid jobId, IDownloadJobStore store) =>
            {
                if (!store.TryGet(jobId, out _))
                {
                    return Results.NotFound();
                }

                store.DeleteJob(jobId);
                return Results.NoContent();
            })
            .WithName("DeleteDownloadJob")
            .WithSummary("Delete a download job");

        group.MapPost("/jobs/{jobId:guid}/install", (
                Guid jobId,
                IDownloadJobStore store,
                IDownloadJobInstallService installService,
                ICloneHeroLibraryService cloneHeroLibraryService,
                IInstallConcurrencyLimiter concurrencyLimiter,
                IHostApplicationLifetime lifetime,
                ITranscriptionJobStore transcriptionJobStore,
                ILogger<DownloadInstallEndpointLogCategory> logger) =>
            {
                if (!store.TryGet(jobId, out DownloadJobResponse? job) || job is null)
                {
                    DownloadEndpointLog.InstallJobNotFound(logger, jobId);
                    return Results.NotFound();
                }

                bool isInstallableStage = string.Equals(job.Stage, "Downloaded", StringComparison.OrdinalIgnoreCase);
                bool isAlreadyInstalling = string.Equals(job.Stage, "InstallQueued", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(job.Stage, "Staging", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(job.Stage, "Installing", StringComparison.OrdinalIgnoreCase);
                if (!isInstallableStage && !isAlreadyInstalling)
                {
                    DownloadEndpointLog.InstallRejectedInvalidStage(logger, job.JobId, job.Stage);
                    return Results.Conflict(new { error = "Job is not in an installable stage." });
                }

                if (isAlreadyInstalling)
                {
                    DownloadEndpointLog.InstallAlreadyInProgress(logger, job.JobId, job.Stage);
                    return Results.Accepted($"/api/v1/downloads/jobs/{jobId:D}", job);
                }

                DownloadEndpointLog.InstallRequestStarted(logger, job.JobId, job.Source, job.SourceId, job.DisplayName, job.DownloadedPath);
                store.UpdateProgress(jobId, "InstallQueued", 88);

                _ = Task.Run(() => ProcessInstallInBackgroundAsync(job.JobId, store, installService, cloneHeroLibraryService, concurrencyLimiter, transcriptionJobStore, lifetime.ApplicationStopping, logger));

                if (!store.TryGet(jobId, out DownloadJobResponse? queuedJob) || queuedJob is null)
                {
                    DownloadEndpointLog.InstallCompletedButReloadMissing(logger, jobId);
                    return Results.NotFound();
                }

                DownloadEndpointLog.InstallQueued(logger, queuedJob.JobId, queuedJob.Stage, queuedJob.ProgressPercent);
                return Results.Accepted($"/api/v1/downloads/jobs/{jobId:D}", queuedJob);
            })
            .WithName("InstallDownloadJob")
            .WithSummary("Queue install pipeline for a downloaded job");

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
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        IReadOnlyList<DownloadJobResponse> jobs = store.List();

                        await context.Response.WriteAsync($"event: jobs\n", cancellationToken).ConfigureAwait(false);
                        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(jobs)}\n\n", cancellationToken)
                            .ConfigureAwait(false);
                        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Expected when client disconnects the SSE stream.
                }
            })
            .WithName("StreamDownloadJobs")
            .WithSummary("Stream full download job state over SSE");

        group.MapGet("/jobs/{jobId:guid}/logs", (Guid jobId, IDownloadJobStore store, IJobLogSink jobLogSink) =>
            {
                if (!store.TryGet(jobId, out _))
                {
                    return Results.NotFound();
                }

                IReadOnlyList<JobLogEntry> logs = jobLogSink.GetLogs(jobId);
                IEnumerable<JobLogEntryResponse> response = logs.Select(l => new JobLogEntryResponse
                {
                    TimestampUtc = l.TimestampUtc,
                    Level = l.Level,
                    EventId = l.EventId,
                    Category = l.Category,
                    Message = l.Message,
                    Exception = l.Exception,
                });
                return Results.Ok(response);
            })
            .WithName("GetDownloadJobLogs")
            .WithSummary("Get structured install logs for a specific download job");

        return group;
    }

    private static async Task ProcessInstallInBackgroundAsync(
        Guid jobId,
        IDownloadJobStore store,
        IDownloadJobInstallService installService,
        ICloneHeroLibraryService cloneHeroLibraryService,
        IInstallConcurrencyLimiter concurrencyLimiter,
        ITranscriptionJobStore transcriptionJobStore,
        CancellationToken cancellationToken,
        ILogger logger)
    {
        if (!store.TryGet(jobId, out DownloadJobResponse? job) || job is null)
        {
            DownloadEndpointLog.InstallJobNotFound(logger, jobId);
            return;
        }

        await concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            store.UpdateProgress(jobId, "Staging", 90);
            DownloadJobInstallResult installResult = await installService.InstallJobAsync(job, cancellationToken).ConfigureAwait(false);
            store.MarkStaged(jobId, installResult.StagedPath);
            store.UpdateProgress(jobId, "Installing", 97);
            (string? sourceMd5, string? sourceChartHash) = ExtractSourceHashes(job.Source, job.SourceId);
            store.MarkInstalled(
                jobId,
                installResult.InstalledPath,
                installResult.InstalledRelativePath,
                installResult.Metadata.Artist,
                installResult.Metadata.Title,
                installResult.Metadata.Charter,
                sourceMd5,
                sourceChartHash);

            cloneHeroLibraryService.UpsertInstalledSong(new CloneHeroLibraryUpsertRequest(
                Source: job.Source,
                SourceId: job.SourceId,
                Artist: installResult.Metadata.Artist,
                Title: installResult.Metadata.Title,
                Charter: installResult.Metadata.Charter,
                SourceMd5: sourceMd5,
                SourceChartHash: sourceChartHash,
                SourceUrl: job.SourceUrl,
                InstalledPath: installResult.InstalledPath,
                InstalledRelativePath: installResult.InstalledRelativePath));

            DownloadEndpointLog.InstallRequestSucceeded(logger, job.JobId, installResult.StagedPath, installResult.InstalledPath);

            // If drum generation was requested and no approved result exists, enqueue a transcription job.
            if (job.DrumGenRequested && transcriptionJobStore.GetLatestApprovedResult(job.JobId.ToString()) is null)
            {
                transcriptionJobStore.CreateJob(job.JobId.ToString(), installResult.InstalledPath, TranscriptionAggressiveness.Medium);
            }
        }
        catch (Exception ex)
        {
            DownloadEndpointLog.InstallRequestFailed(logger, job.JobId, job.Source, job.SourceId, job.DisplayName, job.DownloadedPath, ex);
            store.MarkFailed(jobId, ex.Message);
        }
        finally
        {
            concurrencyLimiter.Release();
        }
    }

    private static (string? Md5, string? ChartHash) ExtractSourceHashes(string source, string sourceId)
    {
        if (!string.Equals(source, "encore", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(sourceId))
        {
            return (null, null);
        }

        string? md5 = null;
        string? chartHash = null;
        string[] parts = sourceId.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            int separator = part.IndexOf('=');
            if (separator <= 0 || separator >= part.Length - 1)
            {
                continue;
            }

            string key = part[..separator].Trim();
            string value = Uri.UnescapeDataString(part[(separator + 1)..].Trim());
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (key.Equals("md5", StringComparison.OrdinalIgnoreCase))
            {
                md5 = value;
            }
            else if (key.Equals("charthash", StringComparison.OrdinalIgnoreCase)
                || key.Equals("chartHash", StringComparison.OrdinalIgnoreCase))
            {
                chartHash = value;
            }
        }

        return (md5, chartHash);
    }

    private static partial class DownloadEndpointLog
    {
        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Warning,
            Message = "Install request failed because job {JobId} was not found.")]
        public static partial void InstallJobNotFound(ILogger logger, Guid jobId);

        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Warning,
            Message = "Install request rejected for job {JobId} because stage '{Stage}' is not installable.")]
        public static partial void InstallRejectedInvalidStage(ILogger logger, Guid jobId, string stage);

        [LoggerMessage(
            EventId = 2003,
            Level = LogLevel.Information,
            Message = "Install request started for job {JobId}. source={Source}, sourceId={SourceId}, displayName='{DisplayName}', downloadedPath='{DownloadedPath}'.")]
        public static partial void InstallRequestStarted(
            ILogger logger,
            Guid jobId,
            string source,
            string sourceId,
            string displayName,
            string? downloadedPath);

        [LoggerMessage(
            EventId = 2007,
            Level = LogLevel.Information,
            Message = "Install request queued for job {JobId}. stage='{Stage}', progress={ProgressPercent}.")]
        public static partial void InstallQueued(ILogger logger, Guid jobId, string stage, double progressPercent);

        [LoggerMessage(
            EventId = 2008,
            Level = LogLevel.Information,
            Message = "Install request for job {JobId} ignored because install is already in progress at stage '{Stage}'.")]
        public static partial void InstallAlreadyInProgress(ILogger logger, Guid jobId, string stage);

        [LoggerMessage(
            EventId = 2004,
            Level = LogLevel.Error,
            Message = "Install request failed for job {JobId}. source={Source}, sourceId={SourceId}, displayName='{DisplayName}', downloadedPath='{DownloadedPath}'.")]
        public static partial void InstallRequestFailed(
            ILogger logger,
            Guid jobId,
            string source,
            string sourceId,
            string displayName,
            string? downloadedPath,
            Exception exception);

        [LoggerMessage(
            EventId = 2005,
            Level = LogLevel.Warning,
            Message = "Install request finished for job {JobId} but the persisted job could not be reloaded.")]
        public static partial void InstallCompletedButReloadMissing(ILogger logger, Guid jobId);

        [LoggerMessage(
            EventId = 2006,
            Level = LogLevel.Information,
            Message = "Install request succeeded for job {JobId}. stagedPath='{StagedPath}', installedPath='{InstalledPath}'.")]
        public static partial void InstallRequestSucceeded(ILogger logger, Guid jobId, string stagedPath, string installedPath);
    }

    private sealed class DownloadInstallEndpointLogCategory;
}
