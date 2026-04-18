using ChartHub.Server.Contracts;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Mvc;

namespace ChartHub.Server.Endpoints;

/// <summary>
/// JWT-authenticated endpoints for managing transcription jobs (scan, list, approve, retry, delete).
/// </summary>
public static class TranscriptionEndpoints
{
    public static RouteGroupBuilder MapTranscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app
            .MapGroup("/api/v1/transcription")
            .RequireAuthorization()
            .WithTags("Transcription");

        // POST /api/v1/transcription/scan
        // Scans the library for songs without drum tracks and enqueues medium-aggressiveness jobs.
        group.MapPost("/scan", (
            ICloneHeroLibraryService library,
            ITranscriptionJobStore jobStore,
            ILogger<TranscriptionEndpointLogCategory> logger) =>
        {
            IReadOnlyList<CloneHeroSongResponse> songs = library.ListSongs();
            IReadOnlyList<TranscriptionJob> existingJobs = jobStore.ListJobs();

            var songIdsWithActiveJobs = existingJobs
                .Where(j => j.Status is TranscriptionJobStatus.Queued or TranscriptionJobStatus.Claimed or TranscriptionJobStatus.Processing)
                .Select(j => j.SongId)
                .ToHashSet();

            int enqueued = 0;
            foreach (CloneHeroSongResponse song in songs)
            {
                if (song.InstalledPath is null)
                {
                    continue;
                }

                if (songIdsWithActiveJobs.Contains(song.SongId))
                {
                    continue;
                }

                // Check if the song MIDI already has a drum track by checking if
                // any transcription result has been approved for this song.
                // (Full MIDI inspection is done at approval time.)
                if (jobStore.GetLatestApprovedResult(song.SongId) is not null)
                {
                    continue;
                }

                jobStore.CreateJob(song.SongId, song.InstalledPath, TranscriptionAggressiveness.Medium);
                enqueued++;
                TranscriptionLog.JobEnqueued(logger, song.SongId, TranscriptionAggressiveness.Medium);
            }

            return Results.Ok(new { enqueuedCount = enqueued });
        })
        .WithName("ScanAndEnqueueTranscriptionJobs")
        .WithSummary("Scan library and enqueue transcription jobs for songs without approved drum results.");

        // GET /api/v1/transcription/jobs
        group.MapGet("/jobs", (
            [FromQuery] string? songId,
            [FromQuery] string? status,
            ITranscriptionJobStore jobStore) =>
        {
            TranscriptionJobStatus? parsedStatus = null;
            if (status is not null && Enum.TryParse<TranscriptionJobStatus>(status, ignoreCase: true, out TranscriptionJobStatus s))
            {
                parsedStatus = s;
            }

            IReadOnlyList<TranscriptionJob> jobs = jobStore.ListJobs(songId, parsedStatus);

            var result = jobs
                .Select(j => new TranscriptionJobSummaryResponse
                {
                    JobId = j.JobId,
                    SongId = j.SongId,
                    Aggressiveness = j.Aggressiveness.ToString(),
                    Status = j.Status.ToString(),
                    ClaimedByRunnerId = j.ClaimedByRunnerId,
                    CreatedAtUtc = j.CreatedAtUtc,
                    ClaimedAtUtc = j.ClaimedAtUtc,
                    CompletedAtUtc = j.CompletedAtUtc,
                    FailureReason = j.FailureReason,
                    AttemptNumber = j.AttemptNumber,
                })
                .ToList();

            return Results.Ok(result);
        })
        .WithName("ListTranscriptionJobs")
        .WithSummary("List transcription jobs, optionally filtered by song or status.");

        // GET /api/v1/transcription/results
        group.MapGet("/results", (
            [FromQuery] string? songId,
            ITranscriptionJobStore jobStore) =>
        {
            IReadOnlyList<TranscriptionResult> results = jobStore.ListResults(songId);
            return Results.Ok(results.Select(r => new TranscriptionResultResponse
            {
                ResultId = r.ResultId,
                JobId = r.JobId,
                SongId = r.SongId,
                Aggressiveness = r.Aggressiveness.ToString(),
                MidiFilePath = r.MidiFilePath,
                CompletedAtUtc = r.CompletedAtUtc,
                IsApproved = r.IsApproved,
                ApprovedAtUtc = r.ApprovedAtUtc,
            }));
        })
        .WithName("ListTranscriptionResults")
        .WithSummary("List completed transcription results.");

        // DELETE /api/v1/transcription/jobs/{jobId}
        group.MapDelete("/jobs/{jobId}", (
            string jobId,
            ITranscriptionJobStore jobStore) =>
        {
            bool deleted = jobStore.DeleteJob(jobId);
            return deleted ? Results.NoContent() : Results.NotFound(new { error = "Job not found." });
        })
        .WithName("DeleteTranscriptionJob")
        .WithSummary("Delete a transcription job.");

        // POST /api/v1/transcription/jobs/{songId}/retry
        // Enqueues a retry job with a specified aggressiveness level.
        group.MapPost("/jobs/{songId}/retry", (
            string songId,
            [FromBody] RetryTranscriptionRequest request,
            ITranscriptionJobStore jobStore,
            ICloneHeroLibraryService library,
            ILogger<TranscriptionEndpointLogCategory> logger) =>
        {
            if (!library.TryGetSong(songId, out CloneHeroSongResponse? song) || song?.InstalledPath is null)
            {
                return Results.NotFound(new { error = "Song not found or not installed." });
            }

            if (!Enum.TryParse<TranscriptionAggressiveness>(request.Aggressiveness, ignoreCase: true, out TranscriptionAggressiveness aggressiveness))
            {
                return Results.BadRequest(new { error = "Invalid aggressiveness. Use Low, Medium, or High." });
            }

            IReadOnlyList<TranscriptionJob> existing = jobStore.ListJobs(songId);
            int attemptNumber = existing.Count + 1;

            TranscriptionJob job = jobStore.CreateJob(song.InstalledPath, songId, aggressiveness, attemptNumber);
            TranscriptionLog.JobEnqueued(logger, songId, aggressiveness);

            return Results.Ok(new TranscriptionJobResponse
            {
                JobId = job.JobId,
                SongId = job.SongId,
                SongFolderPath = job.SongFolderPath,
                Aggressiveness = job.Aggressiveness.ToString(),
                AttemptNumber = job.AttemptNumber,
            });
        })
        .WithName("RetryTranscriptionJob")
        .WithSummary("Enqueue a retry transcription job for a song with a specific aggressiveness.");

        // POST /api/v1/transcription/results/{resultId}/approve
        group.MapPost("/results/{resultId}/approve", (
            string resultId,
            ITranscriptionJobStore jobStore,
            IPostProcessingService postProcessing,
            ILogger<TranscriptionEndpointLogCategory> logger) =>
        {
            IReadOnlyList<TranscriptionResult> allResults = jobStore.ListResults();
            TranscriptionResult? result = allResults.FirstOrDefault(r => r.ResultId == resultId);
            if (result is null)
            {
                return Results.NotFound(new { error = "Result not found." });
            }

            jobStore.ApproveResult(resultId);
            postProcessing.ApplyApprovedDrumResult(result.SongId, result.MidiFilePath);
            TranscriptionLog.ResultApproved(logger, resultId, result.SongId);

            return Results.NoContent();
        })
        .WithName("ApproveTranscriptionResult")
        .WithSummary("Approve a transcription result and merge the generated drum MIDI into the song.");

        return group;
    }
}

/// <summary>Log category marker for transcription endpoints.</summary>
public sealed class TranscriptionEndpointLogCategory;

internal static partial class TranscriptionLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Enqueued transcription job for song {SongId} with aggressiveness {Aggressiveness}.")]
    public static partial void JobEnqueued(ILogger logger, string songId, TranscriptionAggressiveness aggressiveness);

    [LoggerMessage(Level = LogLevel.Information, Message = "Approved transcription result {ResultId} for song {SongId}.")]
    public static partial void ResultApproved(ILogger logger, string resultId, string songId);
}
