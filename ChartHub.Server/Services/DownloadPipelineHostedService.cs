using System.Net;
using System.Net.Http.Headers;

using ChartHub.Server.Contracts;
using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed class DownloadPipelineHostedService(
    IDownloadJobStore jobStore,
    IHttpClientFactory httpClientFactory,
    ISourceUrlResolver sourceUrlResolver,
    IGoogleDriveFolderArchiveService googleDriveFolderArchiveService,
    IWebHostEnvironment environment,
    IOptions<ServerPathOptions> pathOptions,
    IOptions<DownloadsOptions> downloadOptions) : BackgroundService
{
    private readonly IDownloadJobStore _jobStore = jobStore;
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("downloads");
    private readonly ISourceUrlResolver _sourceUrlResolver = sourceUrlResolver;
    private readonly IGoogleDriveFolderArchiveService _googleDriveFolderArchiveService = googleDriveFolderArchiveService;
    private readonly DownloadsOptions _downloadsOptions = downloadOptions.Value;
    private readonly string _downloadsDir = ServerContentPathResolver.Resolve(pathOptions.Value.DownloadsDir, environment.ContentRootPath);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            string downloadedPath = resolvedSource.IsGoogleDriveFolder
                ? await DownloadGoogleDriveFolderAsync(job, resolvedSource, cancellationToken).ConfigureAwait(false)
                : await DownloadFileAsync(job, resolvedSource, cancellationToken).ConfigureAwait(false);
            _jobStore.MarkDownloaded(job.JobId, downloadedPath);
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

    private async Task<string> DownloadFileAsync(DownloadJobResponse job, ResolvedSourceUrl resolvedSource, CancellationToken cancellationToken)
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

        string extension = ResolveDownloadExtension(sourceUri, resolvedSource.SuggestedName, response.Content.Headers);

        string safeBaseName = MakeSafeFileName(resolvedSource.SuggestedName ?? job.DisplayName);
        Directory.CreateDirectory(_downloadsDir);
        string destinationPath = Path.Combine(_downloadsDir, $"{safeBaseName}-{job.JobId:D}{extension}");

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream output = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

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

        return destinationPath;
    }

    private async Task<string> DownloadGoogleDriveFolderAsync(DownloadJobResponse job, ResolvedSourceUrl resolvedSource, CancellationToken cancellationToken)
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
        return downloadedPath;
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

    private static string ResolveDownloadExtension(Uri sourceUri, string? suggestedName, HttpContentHeaders contentHeaders)
    {
        string extension = Path.GetExtension(suggestedName ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension;
        }

        string contentDispositionName = contentHeaders.ContentDisposition?.FileNameStar
            ?? contentHeaders.ContentDisposition?.FileName
            ?? string.Empty;
        contentDispositionName = contentDispositionName.Trim('"');
        extension = Path.GetExtension(contentDispositionName);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension;
        }

        extension = Path.GetExtension(sourceUri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension;
        }

        if (TryInferRhythmVerseExtension(sourceUri, out string inferredExtension))
        {
            return inferredExtension;
        }

        return ".bin";
    }

    private static bool TryInferRhythmVerseExtension(Uri sourceUri, out string extension)
    {
        extension = string.Empty;
        if (!sourceUri.Host.Contains("rhythmverse.co", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string tail = sourceUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        if (tail.EndsWith("_rb3con", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".rb3con";
            return true;
        }

        if (tail.EndsWith("_con", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".con";
            return true;
        }

        if (tail.EndsWith("_rar", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".rar";
            return true;
        }

        if (tail.EndsWith("_zip", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".zip";
            return true;
        }

        if (tail.EndsWith("_7z", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".7z";
            return true;
        }

        return false;
    }

}
