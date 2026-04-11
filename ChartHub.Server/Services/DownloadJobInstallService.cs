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

public sealed record DownloadJobInstallResult(string StagedPath, string InstalledPath);

public sealed partial class DownloadJobInstallService : IDownloadJobInstallService
{
    private readonly string _stagingDir;
    private readonly string _cloneHeroRoot;
    private readonly string _contentRootPath;
    private readonly IServerInstallFileTypeResolver _fileTypeResolver;
    private readonly IServerOnyxInstallService _onyxInstallService;
    private readonly IServerSongIniMetadataParser _songIniParser;
    private readonly IServerCloneHeroDirectorySchemaService _schemaService;
    private readonly ILogger<DownloadJobInstallService> _logger;

    public DownloadJobInstallService(
        IOptions<ServerPathOptions> pathOptions,
        IWebHostEnvironment environment,
        IServerInstallFileTypeResolver fileTypeResolver,
        IServerOnyxInstallService onyxInstallService,
        IServerSongIniMetadataParser songIniParser,
        IServerCloneHeroDirectorySchemaService schemaService,
        ILogger<DownloadJobInstallService> logger)
    {
        ServerPathOptions paths = pathOptions.Value;
        _contentRootPath = environment.ContentRootPath;
        _stagingDir = ServerContentPathResolver.Resolve(paths.StagingDir, _contentRootPath);
        _cloneHeroRoot = ServerContentPathResolver.Resolve(paths.CloneHeroRoot, _contentRootPath);
        _fileTypeResolver = fileTypeResolver;
        _onyxInstallService = onyxInstallService;
        _songIniParser = songIniParser;
        _schemaService = schemaService;
        _logger = logger;
    }

    public async Task<DownloadJobInstallResult> InstallJobAsync(DownloadJobResponse job, CancellationToken cancellationToken = default)
    {
        try
        {
            string artifactPath = ResolveDownloadedArtifactPath(job);
            InstallLog.ArtifactResolved(_logger, job.JobId, job.DownloadedPath, artifactPath);

            if (!File.Exists(artifactPath))
            {
                InstallLog.ArtifactMissing(_logger, job.JobId, artifactPath);
                throw new InvalidOperationException("Downloaded artifact is missing.");
            }

            Directory.CreateDirectory(_cloneHeroRoot);
            InstallLog.CloneHeroRootReady(_logger, job.JobId, _cloneHeroRoot);

            string stagedPath = MoveArtifactToStaging(job.JobId, artifactPath);
            InstallLog.ArtifactMovedToStaging(_logger, job.JobId, stagedPath);

            ServerInstallFileType type = await _fileTypeResolver.ResolveAsync(stagedPath, cancellationToken).ConfigureAwait(false);
            string source = _schemaService.NormalizeSource(job.Source);
            InstallLog.ArtifactTypeResolved(_logger, job.JobId, type, source);

            string installedPath = type switch
            {
                ServerInstallFileType.Zip or ServerInstallFileType.Rar or ServerInstallFileType.SevenZip
                    => await InstallArchiveAsync(stagedPath, source, cancellationToken).ConfigureAwait(false),
                ServerInstallFileType.Con
                    => await InstallOnyxAsync(stagedPath, source, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Unsupported install artifact format."),
            };

            InstallLog.InstallCompleted(_logger, job.JobId, installedPath);
            return new DownloadJobInstallResult(stagedPath, installedPath);
        }
        catch (Exception ex)
        {
            InstallLog.InstallFailed(_logger, job.JobId, job.Source, job.SourceId, job.DisplayName, job.DownloadedPath, ex);
            throw;
        }
    }

    private async Task<string> InstallArchiveAsync(string artifactPath, string source, CancellationToken cancellationToken)
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

            return await RehomeInstalledDirectoryAsync(installWorkspace, source, Path.GetFileNameWithoutExtension(artifactPath), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Cleanup(installWorkspace);
            throw;
        }
    }

    private async Task<string> InstallOnyxAsync(string artifactPath, string source, CancellationToken cancellationToken)
    {
        InstallLog.OnyxInstallStarted(_logger, artifactPath, source);
        ServerOnyxInstallResult result = await _onyxInstallService.ConvertAsync(artifactPath, source, cancellationToken).ConfigureAwait(false);
        InstallLog.OnyxInstallCompleted(_logger, result.OutputDirectory, result.Metadata.Artist, result.Metadata.Title, result.Metadata.Charter);
        return await RehomeInstalledDirectoryAsync(result.OutputDirectory, source, Path.GetFileNameWithoutExtension(artifactPath), cancellationToken, result.Metadata).ConfigureAwait(false);
    }

    private Task<string> RehomeInstalledDirectoryAsync(
        string currentDirectory,
        string source,
        string? fallbackTitle,
        CancellationToken cancellationToken,
        ServerSongMetadata? fallbackMetadata = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(currentDirectory))
        {
            throw new InvalidOperationException("Install output directory was not produced.");
        }

        string? songIniPath = Directory
            .EnumerateFiles(currentDirectory, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetFileName(path).Equals("song.ini", StringComparison.OrdinalIgnoreCase));

        ServerSongMetadata metadata = songIniPath is not null
            ? _songIniParser.ParseFromSongIni(songIniPath)
            : fallbackMetadata ?? new ServerSongMetadata("Unknown Artist", fallbackTitle ?? "Unknown Song", "Unknown Charter");

        ServerCloneHeroDirectoryLayout layout = _schemaService.ResolveUniqueLayout(
            _cloneHeroRoot,
            metadata,
            source,
            exists: path => string.Equals(path, currentDirectory, StringComparison.Ordinal) || Directory.Exists(path));

        if (string.Equals(layout.FullPath, currentDirectory, StringComparison.Ordinal))
        {
            return Task.FromResult(currentDirectory);
        }

        string? parent = Path.GetDirectoryName(layout.FullPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        Directory.Move(currentDirectory, layout.FullPath);
        InstallLog.RehomedInstallDirectory(_logger, currentDirectory, layout.FullPath, layout.RelativePath);
        return Task.FromResult(layout.FullPath);
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
    }
}
