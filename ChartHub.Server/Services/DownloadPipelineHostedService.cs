using System.Net;
using System.Net.Http.Headers;

using ChartHub.Server.Contracts;
using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed partial class DownloadPipelineHostedService(
    IDownloadJobStore jobStore,
    IHttpClientFactory httpClientFactory,
    ISourceUrlResolver sourceUrlResolver,
    IGoogleDriveFolderArchiveService googleDriveFolderArchiveService,
    IServerInstallFileTypeResolver fileTypeResolver,
    ILogger<DownloadPipelineHostedService> logger,
    IWebHostEnvironment environment,
    IOptions<ServerPathOptions> pathOptions,
    IOptions<DownloadsOptions> downloadOptions) : BackgroundService
{
    private readonly IDownloadJobStore _jobStore = jobStore;
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("downloads");
    private readonly ISourceUrlResolver _sourceUrlResolver = sourceUrlResolver;
    private readonly IGoogleDriveFolderArchiveService _googleDriveFolderArchiveService = googleDriveFolderArchiveService;
    private readonly IServerInstallFileTypeResolver _fileTypeResolver = fileTypeResolver;
    private readonly ILogger<DownloadPipelineHostedService> _logger = logger;
    private readonly DownloadsOptions _downloadsOptions = downloadOptions.Value;
    private readonly string _downloadsDir = ServerContentPathResolver.Resolve(pathOptions.Value.DownloadsDir, environment.ContentRootPath);
    private readonly string _contentRootPath = environment.ContentRootPath;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await BackfillFileTypesAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DownloadJobResponse? nextJob = _jobStore.TryClaimNextQueuedJob();
                if (nextJob is not null)
                {
                    await ProcessJobAsync(nextJob, stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    CleanupOldJobs();
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessJobAsync(DownloadJobResponse job, CancellationToken cancellationToken)
    {
        try
        {
            EnsureNotCancelled(job.JobId);
            _jobStore.UpdateProgress(job.JobId, "ResolvingSource", 5);

            ResolvedSourceUrl resolvedSource = await _sourceUrlResolver.ResolveAsync(job.SourceUrl, cancellationToken).ConfigureAwait(false);

            _jobStore.UpdateProgress(job.JobId, "Downloading", 10);
            DownloadedArtifact artifact = resolvedSource.IsGoogleDriveFolder
                ? await DownloadGoogleDriveFolderAsync(job, resolvedSource, cancellationToken).ConfigureAwait(false)
                : await DownloadFileAsync(job, resolvedSource, cancellationToken).ConfigureAwait(false);
            _jobStore.SetDownloadedArtifact(job.JobId, artifact.StoredPath, artifact.Classification.FileType.ToString());
        }
        catch (OperationCanceledException)
        {
            _jobStore.MarkCancelled(job.JobId);
        }
        catch (Exception ex)
        {
            _jobStore.MarkFailed(job.JobId, ex.Message);
        }
    }

    private async Task<DownloadedArtifact> DownloadFileAsync(DownloadJobResponse job, ResolvedSourceUrl resolvedSource, CancellationToken cancellationToken)
    {
        if (resolvedSource.DownloadUri is null)
        {
            throw new InvalidOperationException("Resolved source did not provide a download URI.");
        }

        Uri sourceUri = resolvedSource.DownloadUri;
        using HttpResponseMessage response = await _httpClient
            .GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("Source returned 404.");
        }

        response.EnsureSuccessStatusCode();

        string safeBaseName = MakeSafeFileName(resolvedSource.SuggestedName ?? job.DisplayName);
        Directory.CreateDirectory(_downloadsDir);
        string tempPath = Path.Combine(_downloadsDir, $"{safeBaseName}-{job.JobId:D}.download");

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream output = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        long? totalBytes = response.Content.Headers.ContentLength;
        byte[] buffer = new byte[81920];
        long written = 0;
        while (true)
        {
            EnsureNotCancelled(job.JobId);

            int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;

            if (totalBytes is > 0)
            {
                double ratio = Math.Clamp((double)written / totalBytes.Value, 0d, 1d);
                double progress = 10 + (ratio * 70);
                _jobStore.UpdateProgress(job.JobId, "Downloading", progress);
            }
        }

        return await FinalizeDownloadedArtifactAsync(job, tempPath, safeBaseName, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DownloadedArtifact> DownloadGoogleDriveFolderAsync(DownloadJobResponse job, ResolvedSourceUrl resolvedSource, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resolvedSource.GoogleDriveFolderId))
        {
            throw new InvalidOperationException("Resolved Google Drive folder did not include an identifier.");
        }

        _jobStore.UpdateProgress(job.JobId, "DownloadingFolderZip", 20);
        string downloadedPath = await _googleDriveFolderArchiveService
            .DownloadFolderAsZipAsync(
                resolvedSource.GoogleDriveFolderId,
                resolvedSource.SuggestedName ?? (job.DisplayName + ".zip"),
                _downloadsDir,
                job.JobId,
                cancellationToken)
            .ConfigureAwait(false);
        _jobStore.UpdateProgress(job.JobId, "Downloading", 80);
        return await FinalizeDownloadedArtifactAsync(
            job,
            downloadedPath,
            MakeSafeFileName(resolvedSource.SuggestedName ?? job.DisplayName),
            cancellationToken).ConfigureAwait(false);
    }

    private void EnsureNotCancelled(Guid jobId)
    {
        if (_jobStore.IsCancelRequested(jobId))
        {
            throw new OperationCanceledException($"Job {jobId:D} was cancelled.");
        }
    }

    private void CleanupOldJobs()
    {
        int retentionDays = Math.Max(1, _downloadsOptions.CompletedJobRetentionDays);
        DateTimeOffset threshold = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        _jobStore.DeleteFinishedOlderThan(threshold);
    }

    private static string MakeSafeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "download" : cleaned;
    }

    private async Task BackfillFileTypesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DownloadJobResponse> jobs = _jobStore.List()
            .Where(job => string.Equals(job.Stage, "Downloaded", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(job.DownloadedPath))
            .ToList();

        foreach (DownloadJobResponse job in jobs)
        {
            if (cancellationToken.IsCancellationRequested || job.DownloadedPath is null)
            {
                break;
            }

            try
            {
                string resolvedPath = ResolveArtifactPath(job.DownloadedPath);
                string safeBaseName = Path.GetFileNameWithoutExtension(resolvedPath);
                DownloadedArtifact artifact = await FinalizeDownloadedArtifactAsync(
                    job,
                    resolvedPath,
                    safeBaseName,
                    cancellationToken,
                    deleteUnknownArtifact: false).ConfigureAwait(false);
                _jobStore.SetDownloadedArtifact(job.JobId, artifact.StoredPath, artifact.Classification.FileType.ToString());
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (InvalidOperationException ex) when (string.Equals(ex.Message, UnknownFileTypeMessage, StringComparison.Ordinal))
            {
                DownloadPipelineLog.UnknownArtifactMarkedFailed(_logger, job.JobId, job.DownloadedPath);
                _jobStore.UpdateFileType(job.JobId, ServerInstallFileType.Unknown.ToString());
                _jobStore.MarkFailed(job.JobId, UnknownFileTypeMessage);
            }
            catch (Exception)
            {
                // Best-effort: skip jobs whose artifact is no longer on disk
            }
        }
    }

    private async Task<DownloadedArtifact> FinalizeDownloadedArtifactAsync(
        DownloadJobResponse job,
        string currentPath,
        string safeBaseName,
        CancellationToken cancellationToken,
        bool deleteUnknownArtifact = true)
    {
        ServerArtifactClassification classification = await _fileTypeResolver.ClassifyAsync(currentPath, cancellationToken).ConfigureAwait(false);
        if (!classification.IsKnown)
        {
            if (deleteUnknownArtifact)
            {
                TryDeleteFile(currentPath);
            }

            DownloadPipelineLog.UnknownArtifactSignature(_logger, job.JobId, currentPath);
            throw new InvalidOperationException(UnknownFileTypeMessage);
        }

        string canonicalPath = Path.Combine(
            _downloadsDir,
            $"{NormalizeBaseNameForJob(safeBaseName, job.JobId)}{classification.CanonicalExtension}");
        string finalResolvedPath = currentPath;
        if (!string.Equals(currentPath, canonicalPath, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(canonicalPath)!);
            File.Move(currentPath, canonicalPath, overwrite: true);
            finalResolvedPath = canonicalPath;
            DownloadPipelineLog.ArtifactRenamed(_logger, job.JobId, currentPath, canonicalPath, classification.FileType);
        }

        string storedPath = FormatStoredArtifactPath(job.DownloadedPath, finalResolvedPath);
        DownloadPipelineLog.ArtifactClassified(_logger, job.JobId, finalResolvedPath, classification.FileType, classification.CanonicalExtension);
        return new DownloadedArtifact(storedPath, classification);
    }

    private string ResolveArtifactPath(string configuredPath)
    {
        return ServerContentPathResolver.Resolve(configuredPath, _contentRootPath);
    }

    private string FormatStoredArtifactPath(string? originalPath, string resolvedPath)
    {
        if (string.IsNullOrWhiteSpace(originalPath) || Path.IsPathRooted(originalPath))
        {
            return resolvedPath;
        }

        string relativePath = Path.GetRelativePath(_contentRootPath, resolvedPath);
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string NormalizeBaseNameForJob(string safeBaseName, Guid jobId)
    {
        string jobSuffix = $"-{jobId:D}";
        if (safeBaseName.EndsWith(jobSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return safeBaseName;
        }

        return $"{safeBaseName}{jobSuffix}";
    }

    private sealed record DownloadedArtifact(string StoredPath, ServerArtifactClassification Classification);

    private const string UnknownFileTypeMessage = "Unknown Filetype";

    private static partial class DownloadPipelineLog
    {
        [LoggerMessage(EventId = 2201, Level = LogLevel.Information, Message = "Download job {JobId} classified artifact '{ArtifactPath}' as {ArtifactType} with canonical extension '{CanonicalExtension}'.")]
        public static partial void ArtifactClassified(ILogger logger, Guid jobId, string artifactPath, ServerInstallFileType artifactType, string canonicalExtension);

        [LoggerMessage(EventId = 2202, Level = LogLevel.Information, Message = "Download job {JobId} renamed artifact from '{FromPath}' to '{ToPath}' after signature detection as {ArtifactType}.")]
        public static partial void ArtifactRenamed(ILogger logger, Guid jobId, string fromPath, string toPath, ServerInstallFileType artifactType);

        [LoggerMessage(EventId = 2203, Level = LogLevel.Warning, Message = "Download job {JobId} failed artifact signature validation for '{ArtifactPath}'.")]
        public static partial void UnknownArtifactSignature(ILogger logger, Guid jobId, string artifactPath);

        [LoggerMessage(EventId = 2204, Level = LogLevel.Warning, Message = "Legacy downloaded artifact for job {JobId} at '{ArtifactPath}' was marked failed due to unknown file type.")]
        public static partial void UnknownArtifactMarkedFailed(ILogger logger, Guid jobId, string artifactPath);
    }
}
