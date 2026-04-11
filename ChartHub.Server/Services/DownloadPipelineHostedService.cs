using System.Net;

using ChartHub.Server.Contracts;
using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed class DownloadPipelineHostedService(
    IDownloadJobStore jobStore,
    IHttpClientFactory httpClientFactory,
    ISourceUrlResolver sourceUrlResolver,
    IWebHostEnvironment environment,
    IOptions<ServerPathOptions> pathOptions,
    IOptions<DownloadsOptions> downloadOptions) : BackgroundService
{
    private readonly IDownloadJobStore _jobStore = jobStore;
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("downloads");
    private readonly ISourceUrlResolver _sourceUrlResolver = sourceUrlResolver;
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
            string downloadedPath = await DownloadFileAsync(job, resolvedSource, cancellationToken).ConfigureAwait(false);
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
        Uri sourceUri = resolvedSource.DownloadUri;
        using HttpResponseMessage response = await _httpClient
            .GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("Source returned 404.");
        }

        response.EnsureSuccessStatusCode();

        string extension = Path.GetExtension(sourceUri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

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

}
