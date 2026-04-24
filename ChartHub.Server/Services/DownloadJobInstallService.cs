using ChartHub.Conversion;
using ChartHub.Conversion.Models;

using ChartHub.Server.Contracts;
using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;

namespace ChartHub.Server.Services;

public interface IDownloadJobInstallService
{
    Task<DownloadJobInstallResult> InstallJobAsync(DownloadJobResponse job, CancellationToken cancellationToken = default);
}

public sealed record DownloadJobInstallResult(
    string StagedPath,
    string InstalledPath,
    string InstalledRelativePath,
    ServerSongMetadata Metadata,
    IReadOnlyList<ConversionStatus>? Statuses = null);

public sealed record ServerRehomeInstallResult(
    string InstalledPath,
    string InstalledRelativePath,
    ServerSongMetadata Metadata,
    IReadOnlyList<ConversionStatus> Statuses);

public sealed partial class DownloadJobInstallService : IDownloadJobInstallService
{
    private readonly string _stagingDir;
    private readonly string _cloneHeroRoot;
    private readonly string _contentRootPath;
    private readonly IServerInstallFileTypeResolver _fileTypeResolver;
    private readonly IConversionService _conversionService;
    private readonly IServerSongIniMetadataParser _songIniParser;
    private readonly IServerCloneHeroDirectorySchemaService _schemaService;
    private readonly ILogger<DownloadJobInstallService> _logger;
    private readonly IJobLogSink _jobLogSink;

    public DownloadJobInstallService(
        IOptions<ServerPathOptions> pathOptions,
        IWebHostEnvironment environment,
        IServerInstallFileTypeResolver fileTypeResolver,
        IConversionService conversionService,
        IServerSongIniMetadataParser songIniParser,
        IServerCloneHeroDirectorySchemaService schemaService,
        ILogger<DownloadJobInstallService> logger,
        IJobLogSink jobLogSink)
    {
        ServerPathOptions paths = pathOptions.Value;
        _contentRootPath = environment.ContentRootPath;
        _stagingDir = ServerContentPathResolver.Resolve(paths.StagingDir, _contentRootPath);
        _cloneHeroRoot = ServerContentPathResolver.Resolve(paths.CloneHeroRoot, _contentRootPath);
        _fileTypeResolver = fileTypeResolver;
        _conversionService = conversionService;
        _songIniParser = songIniParser;
        _schemaService = schemaService;
        _logger = logger;
        _jobLogSink = jobLogSink;
    }

    public async Task<DownloadJobInstallResult> InstallJobAsync(DownloadJobResponse job, CancellationToken cancellationToken = default)
    {
        try
        {
            string artifactPath = ResolveDownloadedArtifactPath(job);
            InstallLog.ArtifactResolved(_logger, job.JobId, job.DownloadedPath, artifactPath);
            _jobLogSink.Add(job.JobId, LogLevel.Information, new EventId(2101), nameof(DownloadJobInstallService),
                $"Resolved downloaded path '{job.DownloadedPath}' to '{artifactPath}'.", null);

            if (!File.Exists(artifactPath))
            {
                InstallLog.ArtifactMissing(_logger, job.JobId, artifactPath);
                _jobLogSink.Add(job.JobId, LogLevel.Error, new EventId(2102), nameof(DownloadJobInstallService),
                    $"Artifact missing at '{artifactPath}'.", null);
                throw new InvalidOperationException("Downloaded artifact is missing.");
            }

            Directory.CreateDirectory(_cloneHeroRoot);
            InstallLog.CloneHeroRootReady(_logger, job.JobId, _cloneHeroRoot);
            _jobLogSink.Add(job.JobId, LogLevel.Information, new EventId(2103), nameof(DownloadJobInstallService),
                $"Clone Hero root ready at '{_cloneHeroRoot}'.", null);

            string stagedPath = MoveArtifactToStaging(job.JobId, artifactPath);
            InstallLog.ArtifactMovedToStaging(_logger, job.JobId, stagedPath);
            _jobLogSink.Add(job.JobId, LogLevel.Information, new EventId(2104), nameof(DownloadJobInstallService),
                $"Artifact moved to staging at '{stagedPath}'.", null);

            ServerInstallFileType type = await _fileTypeResolver.ResolveAsync(stagedPath, cancellationToken).ConfigureAwait(false);
            string source = _schemaService.NormalizeSource(job.Source);
            InstallLog.ArtifactTypeResolved(_logger, job.JobId, type, source);
            _jobLogSink.Add(job.JobId, LogLevel.Information, new EventId(2105), nameof(DownloadJobInstallService),
                $"Artifact type '{type}' detected for source '{source}'.", null);

            ServerRehomeInstallResult installedPath = type switch
            {
                ServerInstallFileType.Zip or ServerInstallFileType.Rar or ServerInstallFileType.SevenZip
                    => await InstallArchiveAsync(job.JobId, stagedPath, source, cancellationToken).ConfigureAwait(false),
                ServerInstallFileType.Con
                    => await InstallConAsync(job, stagedPath, source, cancellationToken).ConfigureAwait(false),
                ServerInstallFileType.Sng
                    => await InstallSngAsync(job, stagedPath, source, cancellationToken).ConfigureAwait(false),
                ServerInstallFileType.EncryptedSng
                    => throw new InvalidOperationException("SNG artifact appears encrypted or uses an unsupported official variant."),
                _ => throw new InvalidOperationException("Unsupported install artifact format."),
            };

            InstallLog.InstallCompleted(_logger, job.JobId, installedPath.InstalledPath);
            _jobLogSink.Add(job.JobId, LogLevel.Information, new EventId(2106), nameof(DownloadJobInstallService),
                $"Install completed. Installed at '{installedPath.InstalledPath}'.", null);

            return new DownloadJobInstallResult(
                stagedPath,
                installedPath.InstalledPath,
                installedPath.InstalledRelativePath,
                installedPath.Metadata,
                installedPath.Statuses);
        }
        catch (Exception ex)
        {
            InstallLog.InstallFailed(_logger, job.JobId, job.Source, job.SourceId, job.DisplayName, job.DownloadedPath, ex);
            _jobLogSink.Add(job.JobId, LogLevel.Error, new EventId(2107), nameof(DownloadJobInstallService),
                $"Install failed: {ex.Message}", ex.ToString());
            throw;
        }
    }

    private async Task<ServerRehomeInstallResult> InstallArchiveAsync(Guid jobId, string artifactPath, string source, CancellationToken cancellationToken)
    {
        string installWorkspace = Path.Combine(_stagingDir, "install", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installWorkspace);

        try
        {
            using IArchive archive = OpenArchive(artifactPath);
            foreach (IArchiveEntry entry in archive.Entries.Where(item => !item.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string outputPath = ServerSafePathHelper.GetSafeArchiveExtractionPath(installWorkspace, entry.Key, "archive-entry");
                string? outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                await Task.Run(() => entry.WriteToFile(outputPath, new ExtractionOptions
                {
                    ExtractFullPath = false,
                    Overwrite = true,
                }), cancellationToken).ConfigureAwait(false);
            }

            return await RehomeInstalledDirectoryAsync(jobId, installWorkspace, source, Path.GetFileNameWithoutExtension(artifactPath), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Cleanup(installWorkspace);
            throw;
        }
    }

    private async Task<ServerRehomeInstallResult> InstallConAsync(DownloadJobResponse job, string artifactPath, string source, CancellationToken cancellationToken)
    {
        Guid jobId = job.JobId;
        InstallLog.OnyxInstallStarted(_logger, artifactPath, source);
        _jobLogSink.Add(jobId, LogLevel.Information, new EventId(2108), nameof(DownloadJobInstallService),
            $"CON install started for '{artifactPath}', source '{source}'.", null);

        string outputRoot = Path.Combine(_stagingDir, "con", jobId.ToString("N"));
        Directory.CreateDirectory(outputRoot);
        ConversionResult result = await _conversionService.ConvertAsync(artifactPath, outputRoot, cancellationToken).ConfigureAwait(false);
        ServerSongMetadata serverMetadata = BuildFallbackMetadata(job, result.Metadata);
        LogConversionStatuses(jobId, result.Statuses);

        InstallLog.OnyxInstallCompleted(_logger, result.OutputDirectory, result.Metadata.Artist, result.Metadata.Title, result.Metadata.Charter);
        _jobLogSink.Add(jobId, LogLevel.Information, new EventId(2109), nameof(DownloadJobInstallService),
            $"CON install finished. Output '{result.OutputDirectory}', artist='{result.Metadata.Artist}', title='{result.Metadata.Title}', charter='{result.Metadata.Charter}'.", null);

        return await RehomeInstalledDirectoryAsync(
            jobId,
            result.OutputDirectory,
            source,
            Path.GetFileNameWithoutExtension(artifactPath),
            cancellationToken,
            serverMetadata,
            result.Statuses).ConfigureAwait(false);
    }

    private async Task<ServerRehomeInstallResult> InstallSngAsync(DownloadJobResponse job, string artifactPath, string source, CancellationToken cancellationToken)
    {
        Guid jobId = job.JobId;
        InstallLog.SngInstallStarted(_logger, artifactPath, source);
        _jobLogSink.Add(jobId, LogLevel.Information, new EventId(2111), nameof(DownloadJobInstallService),
            $"SNG install started for '{artifactPath}', source '{source}'.", null);

        string outputRoot = Path.Combine(_stagingDir, "sng", jobId.ToString("N"));
        Directory.CreateDirectory(outputRoot);
        ConversionResult result = await _conversionService.ConvertAsync(artifactPath, outputRoot, cancellationToken).ConfigureAwait(false);
        ServerSongMetadata serverMetadata = BuildFallbackMetadata(job, result.Metadata);
        LogConversionStatuses(jobId, result.Statuses);

        InstallLog.SngInstallCompleted(_logger, result.OutputDirectory, result.Metadata.Artist, result.Metadata.Title, result.Metadata.Charter);
        _jobLogSink.Add(jobId, LogLevel.Information, new EventId(2112), nameof(DownloadJobInstallService),
            $"SNG install finished. Output '{result.OutputDirectory}', artist='{result.Metadata.Artist}', title='{result.Metadata.Title}', charter='{result.Metadata.Charter}'.", null);

        return await RehomeInstalledDirectoryAsync(
            jobId,
            result.OutputDirectory,
            source,
            Path.GetFileNameWithoutExtension(artifactPath),
            cancellationToken,
            serverMetadata,
            result.Statuses).ConfigureAwait(false);
    }

    private static ServerSongMetadata BuildFallbackMetadata(DownloadJobResponse job, ConversionMetadata metadata)
    {
        return new ServerSongMetadata(
            ResolveFallbackValue(metadata.Artist, job.RequestedArtist, "Unknown Artist"),
            ResolveFallbackValue(metadata.Title, job.RequestedTitle, "Unknown Song"),
            ResolveFallbackValue(metadata.Charter, job.RequestedCharter, "Unknown Charter"));
    }

    private static string ResolveFallbackValue(string? primary, string? fallback, string defaultValue)
    {
        if (!string.IsNullOrWhiteSpace(primary)
            && !string.Equals(primary, defaultValue, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(primary, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return primary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback)
            && !string.Equals(fallback, defaultValue, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fallback, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return fallback.Trim();
        }

        return defaultValue;
    }

    private Task<ServerRehomeInstallResult> RehomeInstalledDirectoryAsync(
        Guid jobId,
        string currentDirectory,
        string source,
        string? fallbackTitle,
        CancellationToken cancellationToken,
        ServerSongMetadata? fallbackMetadata = null,
        IReadOnlyList<ConversionStatus>? statuses = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(currentDirectory))
        {
            throw new InvalidOperationException("Install output directory was not produced.");
        }

        string? songIniPath = Directory
            .EnumerateFiles(currentDirectory, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetFileName(path).Equals("song.ini", StringComparison.OrdinalIgnoreCase));

        ServerSongMetadata parsedMetadata = songIniPath is not null
            ? _songIniParser.ParseFromSongIni(songIniPath)
            : new ServerSongMetadata("Unknown Artist", fallbackTitle ?? "Unknown Song", "Unknown Charter");
        ServerSongMetadata metadata = MergeMetadataWithFallback(parsedMetadata, fallbackMetadata);

        // If the archive had a top-level subdirectory, song.ini will be found inside it rather than
        // directly in the staging workspace root. Move only the song.ini's parent so that extra
        // nesting is not carried into the final install location.
        string sourceToMove = songIniPath is not null
            ? Path.GetDirectoryName(songIniPath)!
            : currentDirectory;
        if (string.Equals(sourceToMove, currentDirectory, StringComparison.Ordinal))
        {
            sourceToMove = currentDirectory;
        }

        ServerCloneHeroDirectoryLayout layout = _schemaService.ResolveUniqueLayout(
            _cloneHeroRoot,
            metadata,
            source,
            exists: path => string.Equals(path, sourceToMove, StringComparison.Ordinal) || Directory.Exists(path));

        if (string.Equals(layout.FullPath, sourceToMove, StringComparison.Ordinal))
        {
            return Task.FromResult(new ServerRehomeInstallResult(sourceToMove, layout.RelativePath, metadata, statuses ?? []));
        }

        string? parent = Path.GetDirectoryName(layout.FullPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        Directory.Move(sourceToMove, layout.FullPath);
        InstallLog.RehomedInstallDirectory(_logger, sourceToMove, layout.FullPath, layout.RelativePath);
        _jobLogSink.Add(jobId, LogLevel.Information, new EventId(2110), nameof(DownloadJobInstallService),
            $"Rehomed from '{sourceToMove}' to '{layout.FullPath}' (relative '{layout.RelativePath}').", null);

        // If the staging workspace contained a subdirectory that was moved individually, clean up
        // the now-empty workspace root.
        if (!string.Equals(sourceToMove, currentDirectory, StringComparison.Ordinal))
        {
            Cleanup(currentDirectory);
        }

        return Task.FromResult(new ServerRehomeInstallResult(layout.FullPath, layout.RelativePath, metadata, statuses ?? []));
    }

    private static ServerSongMetadata MergeMetadataWithFallback(ServerSongMetadata primary, ServerSongMetadata? fallback)
    {
        if (fallback is null)
        {
            return primary;
        }

        return new ServerSongMetadata(
            ResolveFallbackValue(primary.Artist, fallback.Artist, "Unknown Artist"),
            ResolveFallbackValue(primary.Title, fallback.Title, "Unknown Song"),
            ResolveFallbackValue(primary.Charter, fallback.Charter, "Unknown Charter"));
    }

    private void LogConversionStatuses(Guid jobId, IReadOnlyList<ConversionStatus>? statuses)
    {
        if (statuses is null || statuses.Count == 0)
        {
            return;
        }

        foreach (ConversionStatus status in statuses)
        {
            if (string.Equals(status.Code, ConversionStatusCodes.AudioIncomplete, StringComparison.OrdinalIgnoreCase))
            {
                InstallLog.AudioIncomplete(_logger, jobId, status.Message);
                _jobLogSink.Add(jobId, LogLevel.Warning, new EventId(2113), nameof(DownloadJobInstallService),
                    $"Conversion status '{status.Code}': {status.Message}", null);
                continue;
            }

            InstallLog.ConversionStatus(_logger, jobId, status.Code, status.Message);
            _jobLogSink.Add(jobId, LogLevel.Information, new EventId(2114), nameof(DownloadJobInstallService),
                $"Conversion status '{status.Code}': {status.Message}", null);
        }
    }

    private string MoveArtifactToStaging(Guid jobId, string artifactPath)
    {
        string stageDirectory = Path.Combine(_stagingDir, "jobs", jobId.ToString("D"));
        Directory.CreateDirectory(stageDirectory);

        string destinationPath = Path.Combine(stageDirectory, Path.GetFileName(artifactPath));
        if (string.Equals(artifactPath, destinationPath, StringComparison.Ordinal))
        {
            return destinationPath;
        }

        File.Move(artifactPath, destinationPath, overwrite: true);
        return destinationPath;
    }

    private string ResolveDownloadedArtifactPath(DownloadJobResponse job)
    {
        if (!string.IsNullOrWhiteSpace(job.DownloadedPath))
        {
            return ServerContentPathResolver.Resolve(job.DownloadedPath, _contentRootPath);
        }

        throw new InvalidOperationException("Job does not have a downloaded artifact path.");
    }

    private static IArchive OpenArchive(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".zip" => ZipArchive.Open(filePath),
            ".rar" => RarArchive.Open(filePath),
            ".7z" => SevenZipArchive.Open(filePath),
            _ => throw new NotSupportedException($"File type {extension} is not supported.")
        };
    }

    private static void Cleanup(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static partial class InstallLog
    {
        [LoggerMessage(EventId = 2101, Level = LogLevel.Information, Message = "Install job {JobId} resolved downloaded path '{ConfiguredPath}' to '{ResolvedPath}'.")]
        public static partial void ArtifactResolved(ILogger logger, Guid jobId, string? configuredPath, string resolvedPath);

        [LoggerMessage(EventId = 2102, Level = LogLevel.Error, Message = "Install job {JobId} artifact missing at '{ArtifactPath}'.")]
        public static partial void ArtifactMissing(ILogger logger, Guid jobId, string artifactPath);

        [LoggerMessage(EventId = 2103, Level = LogLevel.Information, Message = "Install job {JobId} ensured Clone Hero root '{CloneHeroRoot}'.")]
        public static partial void CloneHeroRootReady(ILogger logger, Guid jobId, string cloneHeroRoot);

        [LoggerMessage(EventId = 2104, Level = LogLevel.Information, Message = "Install job {JobId} moved artifact to staging path '{StagedPath}'.")]
        public static partial void ArtifactMovedToStaging(ILogger logger, Guid jobId, string stagedPath);

        [LoggerMessage(EventId = 2105, Level = LogLevel.Information, Message = "Install job {JobId} detected artifact type '{ArtifactType}' for source '{Source}'.")]
        public static partial void ArtifactTypeResolved(ILogger logger, Guid jobId, ServerInstallFileType artifactType, string source);

        [LoggerMessage(EventId = 2106, Level = LogLevel.Information, Message = "Install job completed for {JobId}. Installed path '{InstalledPath}'.")]
        public static partial void InstallCompleted(ILogger logger, Guid jobId, string installedPath);

        [LoggerMessage(EventId = 2107, Level = LogLevel.Error, Message = "Install job failed for {JobId}. source={Source}, sourceId={SourceId}, displayName='{DisplayName}', downloadedPath='{DownloadedPath}'.")]
        public static partial void InstallFailed(
            ILogger logger,
            Guid jobId,
            string source,
            string sourceId,
            string displayName,
            string? downloadedPath,
            Exception exception);

        [LoggerMessage(EventId = 2108, Level = LogLevel.Information, Message = "Onyx install started for artifact '{ArtifactPath}' and source '{Source}'.")]
        public static partial void OnyxInstallStarted(ILogger logger, string artifactPath, string source);

        [LoggerMessage(EventId = 2109, Level = LogLevel.Information, Message = "Onyx install produced output '{OutputDirectory}' with metadata artist='{Artist}', title='{Title}', charter='{Charter}'.")]
        public static partial void OnyxInstallCompleted(ILogger logger, string outputDirectory, string artist, string title, string charter);

        [LoggerMessage(EventId = 2110, Level = LogLevel.Information, Message = "Rehomed install directory from '{FromPath}' to '{ToPath}' (relative '{RelativePath}').")]
        public static partial void RehomedInstallDirectory(ILogger logger, string fromPath, string toPath, string relativePath);

        [LoggerMessage(EventId = 2111, Level = LogLevel.Information, Message = "SNG install started for artifact '{ArtifactPath}' and source '{Source}'.")]
        public static partial void SngInstallStarted(ILogger logger, string artifactPath, string source);

        [LoggerMessage(EventId = 2112, Level = LogLevel.Information, Message = "SNG install produced output '{OutputDirectory}' with metadata artist='{Artist}', title='{Title}', charter='{Charter}'.")]
        public static partial void SngInstallCompleted(ILogger logger, string outputDirectory, string artist, string title, string charter);

        [LoggerMessage(EventId = 2113, Level = LogLevel.Warning, Message = "Install job {JobId} completed with audio fallback: {Message}")]
        public static partial void AudioIncomplete(ILogger logger, Guid jobId, string message);

        [LoggerMessage(EventId = 2114, Level = LogLevel.Information, Message = "Install job {JobId} reported conversion status {Code}: {Message}")]
        public static partial void ConversionStatus(ILogger logger, Guid jobId, string code, string message);
    }
}
