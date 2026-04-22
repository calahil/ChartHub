using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;

using ChartHub.Server.Contracts;
using ChartHub.Server.Middleware;
using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Endpoints;

/// <summary>
/// Endpoints used by transcription runner agents.
/// All routes except <c>/register</c> require <c>Authorization: Runner {id}:{secret}</c>.
/// </summary>
public static class RunnerProtocolEndpoints
{
    /// <summary>
    /// How long HMAC-signed audio download URLs remain valid.
    /// </summary>
    private static readonly TimeSpan AudioUrlTtl = TimeSpan.FromMinutes(30);
    private const int MaxClaimWaitMs = 60_000;
    private const int ClaimPollSliceMs = 1_000;

    public static RouteGroupBuilder MapRunnerProtocolEndpoints(this IEndpointRouteBuilder app)
    {
        // /register is public (token-based auth inside the handler).
        RouteGroupBuilder publicGroup = app
            .MapGroup("/api/v1/runner")
            .WithTags("RunnerProtocol");

        publicGroup.MapPost("/register", (
            [FromBody] RegisterRunnerRequest request,
            ITranscriptionRunnerRegistry registry) =>
        {
            if (string.IsNullOrWhiteSpace(request.RunnerName))
            {
                return Results.BadRequest(new { error = "runner_name_required" });
            }

            if (string.IsNullOrWhiteSpace(request.RegistrationToken))
            {
                return Results.BadRequest(new { error = "registration_token_required" });
            }

            if (string.IsNullOrWhiteSpace(request.Secret))
            {
                return Results.BadRequest(new { error = "secret_required" });
            }

            try
            {
                RegisterRunnerResponse response = registry.RegisterRunner(
                    request.RunnerName,
                    request.RegistrationToken,
                    request.Secret,
                    request.MaxConcurrency);
                return Results.Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(
                    detail: "Invalid or expired registration token.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("RegisterRunner")
        .WithSummary("Exchange a one-time registration token for a permanent runner identity.")
        .Produces<RegisterRunnerResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        // All other routes require runner authentication.
        RouteGroupBuilder authedGroup = app
            .MapGroup("/api/v1/runner")
            .WithTags("RunnerProtocol")
            .RequireRunnerAuth();

        authedGroup.MapPost("/heartbeat", (
            HttpContext ctx,
            [FromBody] RunnerHeartbeatRequest request,
            ITranscriptionRunnerRegistry registry) =>
        {
            registry.RecordHeartbeat(ctx.GetRunnerId(), request.ActiveJobCount);
            return Results.NoContent();
        })
        .WithName("RunnerHeartbeat")
        .WithSummary("Update runner online status and active job count.");

        authedGroup.MapPost("/jobs/claim", async (
            HttpContext ctx,
            [FromQuery] int? waitMs,
            ITranscriptionJobStore jobStore,
            CancellationToken cancellationToken) =>
        {
            int requestedWaitMs = Math.Clamp(waitMs ?? 0, 0, MaxClaimWaitMs);
            if (requestedWaitMs <= 0)
            {
                TranscriptionJob? immediateJob = jobStore.TryClaimNext(ctx.GetRunnerId());
                return immediateJob is null ? Results.NoContent() : Results.Ok(MapJobResponse(immediateJob));
            }

            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(requestedWaitMs);
            while (!cancellationToken.IsCancellationRequested)
            {
                TranscriptionJob? job = jobStore.TryClaimNext(ctx.GetRunnerId());
                if (job is not null)
                {
                    return Results.Ok(MapJobResponse(job));
                }

                TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                int delayMs = (int)Math.Min(remaining.TotalMilliseconds, ClaimPollSliceMs);
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            return Results.NoContent();
        })
        .WithName("ClaimNextTranscriptionJob")
        .WithSummary("Claim the next pending transcription job. Optional waitMs enables long-poll behavior.");

        authedGroup.MapPost("/jobs/{jobId}/processing", (
            string jobId,
            ITranscriptionJobStore jobStore) =>
        {
            jobStore.UpdateStatus(jobId, TranscriptionJobStatus.Processing);
            return Results.NoContent();
        })
        .WithName("MarkJobProcessing")
        .WithSummary("Notify server that runner has started processing the job.");

        authedGroup.MapGet("/jobs/{jobId}/audio", (
            HttpContext ctx,
            string jobId,
            [FromQuery] string sig,
            [FromQuery] long exp,
            ITranscriptionJobStore jobStore,
            IOptions<ServerPathOptions> serverPathOptions) =>
        {
            // Validate HMAC signature.
            string signingKey = serverPathOptions.Value.RunnerAudioSigningKey;
            if (!VerifyAudioSignature(jobId, exp, sig, signingKey))
            {
                return Results.Problem(
                    detail: "Invalid or expired audio download signature.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp)
            {
                return Results.Problem(
                    detail: "Audio download URL has expired.",
                    statusCode: StatusCodes.Status410Gone);
            }

            TranscriptionJob? job = jobStore.GetJob(jobId);
            if (job is null)
            {
                return Results.NotFound(new { error = "Job not found." });
            }

            // The audio is the song folder's OGG/MP3 file.
            string? audioFile = FindAudioFile(job.SongFolderPath);
            if (audioFile is null || !File.Exists(audioFile))
            {
                return Results.NotFound(new { error = "Audio file not found for this song." });
            }

            string mimeType = audioFile.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
                ? "audio/ogg"
                : "audio/mpeg";

            return Results.File(audioFile, mimeType, Path.GetFileName(audioFile));
        })
        .WithName("DownloadJobAudio")
        .WithSummary("Download the source audio file for a transcription job (requires signed URL).");

        authedGroup.MapPost("/jobs/{jobId}/audio-url", (
            HttpContext ctx,
            string jobId,
            ITranscriptionJobStore jobStore,
            IOptions<ServerPathOptions> serverPathOptions,
            LinkGenerator linkGenerator) =>
        {
            TranscriptionJob? job = jobStore.GetJob(jobId);
            if (job is null)
            {
                return Results.NotFound(new { error = "Job not found." });
            }

            // Only the claiming runner should download the audio.
            if (job.ClaimedByRunnerId != ctx.GetRunnerId())
            {
                return Results.Problem(
                    detail: "You do not own this job.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            long exp = DateTimeOffset.UtcNow.Add(AudioUrlTtl).ToUnixTimeSeconds();
            string sig = ComputeAudioSignature(jobId, exp, serverPathOptions.Value.RunnerAudioSigningKey);
            string url = $"/api/v1/runner/jobs/{jobId}/audio?sig={Uri.EscapeDataString(sig)}&exp={exp}";

            return Results.Ok(new { url, expiresAtUnix = exp });
        })
        .WithName("GetJobAudioSignedUrl")
        .WithSummary("Obtain a short-lived signed URL to download the job's source audio.");

        authedGroup.MapPost("/jobs/{jobId}/complete", async (
            HttpContext ctx,
            string jobId,
            ITranscriptionJobStore jobStore,
            IOptions<ServerPathOptions> serverPathOptions,
            ILogger<RunnerProtocolEndpointLogCategory> logger) =>
        {
            TranscriptionJob? job = jobStore.GetJob(jobId);
            if (job is null)
            {
                return Results.NotFound(new { error = "Job not found." });
            }

            if (job.ClaimedByRunnerId != ctx.GetRunnerId())
            {
                return Results.Problem(
                    detail: "You do not own this job.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (!ctx.Request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Expected multipart/form-data with 'midi' file." });
            }

            IFormFile? midiFile = ctx.Request.Form.Files["midi"];
            if (midiFile is null)
            {
                return Results.BadRequest(new { error = "Missing 'midi' form file." });
            }

            // Validate MIDI magic bytes.
            byte[] header = new byte[4];
            using Stream midiStream = midiFile.OpenReadStream();
            int bytesRead = await midiStream.ReadAsync(header.AsMemory(0, 4)).ConfigureAwait(false);
            if (bytesRead < 4 || header[0] != 0x4D || header[1] != 0x54 || header[2] != 0x68 || header[3] != 0x64)
            {
                return Results.BadRequest(new { error = "Uploaded file is not a valid MIDI file." });
            }

            // Save to post-process results dir.
            string ppRoot = serverPathOptions.Value.CloneHeroPostProcessRoot;
            string songResultsDir = Path.Combine(ppRoot, "midi-results", job.SongId);
            Directory.CreateDirectory(songResultsDir);
            string destPath = Path.Combine(songResultsDir, $"{job.Aggressiveness}.mid");

            midiStream.Seek(0, SeekOrigin.Begin);
            await using (FileStream fs = new(destPath, FileMode.Create, FileAccess.Write))
            {
                await midiStream.CopyToAsync(fs).ConfigureAwait(false);
            }

            jobStore.MarkCompleted(jobId, destPath);
            RunnerProtocolLog.JobCompleted(logger, jobId, destPath);

            return Results.NoContent();
        })
        .WithName("CompleteTranscriptionJob")
        .WithSummary("Upload the completed MIDI result for a transcription job.");

        authedGroup.MapPost("/jobs/{jobId}/yield", (
            string jobId,
            ITranscriptionJobStore jobStore) =>
        {
            jobStore.UpdateStatus(jobId, TranscriptionJobStatus.Yielded);
            return Results.NoContent();
        })
        .WithName("YieldTranscriptionJob")
        .WithSummary("Yield an in-progress job back to the queue (e.g. runner shutting down).");

        authedGroup.MapPost("/jobs/{jobId}/fail", (
            string jobId,
            [FromBody] TranscriptionJobFailRequest request,
            ITranscriptionJobStore jobStore) =>
        {
            jobStore.UpdateStatus(jobId, TranscriptionJobStatus.Failed, request.Reason);
            return Results.NoContent();
        })
        .WithName("FailTranscriptionJob")
        .WithSummary("Mark a transcription job as failed.");

        return authedGroup;
    }

    private static TranscriptionJobResponse MapJobResponse(TranscriptionJob job) => new()
    {
        JobId = job.JobId,
        SongId = job.SongId,
        SongFolderPath = job.SongFolderPath,
        Aggressiveness = job.Aggressiveness.ToString(),
        AttemptNumber = job.AttemptNumber,
    };

    private static string? FindAudioFile(string songFolderPath)
    {
        if (!Directory.Exists(songFolderPath))
        {
            return null;
        }

        string[] candidates = Directory.GetFiles(songFolderPath, "*.ogg");
        if (candidates.Length > 0)
        {
            return candidates[0];
        }

        candidates = Directory.GetFiles(songFolderPath, "*.mp3");
        return candidates.Length > 0 ? candidates[0] : null;
    }

    private static string ComputeAudioSignature(string jobId, long expUnix, string signingKey)
    {
        string payload = $"{jobId}:{expUnix}";
        byte[] keyBytes = Encoding.UTF8.GetBytes(signingKey);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        byte[] hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static bool VerifyAudioSignature(string jobId, long expUnix, string sig, string signingKey)
    {
        string expected = ComputeAudioSignature(jobId, expUnix, signingKey);
        // Constant-time comparison to prevent timing attacks.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(sig));
    }
}

/// <summary>Log category marker for runner protocol endpoints.</summary>
public sealed class RunnerProtocolEndpointLogCategory;

internal static partial class RunnerProtocolLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Transcription job {JobId} completed. MIDI saved to {MidiPath}.")]
    public static partial void JobCompleted(ILogger logger, string jobId, string midiPath);
}
