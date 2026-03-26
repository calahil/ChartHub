using System.Net;

using ChartHub.BackupApi.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.BackupApi.Services;

public sealed partial class ImageProxyService : IImageProxyService
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
    };

    private readonly string _cacheRootPath;
    private readonly Uri _upstreamBaseUri;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ImageProxyService> _logger;

    public ImageProxyService(
        HttpClient httpClient,
        IOptions<ImageCacheOptions> imageCacheOptions,
        IOptions<RhythmVerseSourceOptions> sourceOptions,
        IHostEnvironment hostEnvironment,
        ILogger<ImageProxyService> logger)
    {
        ArgumentNullException.ThrowIfNull(imageCacheOptions);
        ArgumentNullException.ThrowIfNull(sourceOptions);
        ArgumentNullException.ThrowIfNull(hostEnvironment);

        _httpClient = httpClient;
        _logger = logger;

        string cacheDirectory = imageCacheOptions.Value.CacheDirectory;
        _cacheRootPath = Path.IsPathRooted(cacheDirectory)
            ? cacheDirectory
            : Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath, cacheDirectory));

        _upstreamBaseUri = new Uri(sourceOptions.Value.BaseUrl, UriKind.Absolute);
    }

    public async Task<ImageProxyResult?> GetImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (!TryNormalizePath(imagePath, out string? normalizedPath, out string? cacheFilePath)
            || normalizedPath is null
            || cacheFilePath is null)
        {
            return null;
        }

        if (File.Exists(cacheFilePath))
        {
            byte[] cachedBytes = await File.ReadAllBytesAsync(cacheFilePath, cancellationToken).ConfigureAwait(false);
            return new ImageProxyResult(cachedBytes, GetContentType(cacheFilePath));
        }

        Uri requestUri = new(_upstreamBaseUri, normalizedPath);

        try
        {
            using HttpResponseMessage response = await _httpClient
                .GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                LogUpstreamRequestFailed(_logger, normalizedPath, (int)response.StatusCode);
                return null;
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            string? directoryPath = Path.GetDirectoryName(cacheFilePath);
            if (directoryPath is null)
            {
                return null;
            }

            Directory.CreateDirectory(directoryPath);
            await File.WriteAllBytesAsync(cacheFilePath, bytes, cancellationToken).ConfigureAwait(false);

            return new ImageProxyResult(bytes, GetContentType(cacheFilePath));
        }
        catch (HttpRequestException ex)
        {
            LogUpstreamRequestThrew(_logger, normalizedPath, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Image proxy upstream request failed for {ImagePath} with status code {StatusCode}")]
    private static partial void LogUpstreamRequestFailed(ILogger logger, string imagePath, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Image proxy upstream request threw for {ImagePath}")]
    private static partial void LogUpstreamRequestThrew(ILogger logger, string imagePath, Exception exception);

    private bool TryNormalizePath(string imagePath, out string? normalizedPath, out string? cacheFilePath)
    {
        normalizedPath = null;
        cacheFilePath = null;

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return false;
        }

        string trimmedPath = imagePath.Trim();
        if (Uri.TryCreate(trimmedPath, UriKind.Absolute, out _))
        {
            return false;
        }

        string[] segments = trimmedPath
            .Replace('\\', '/')
            .TrimStart('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2)
        {
            return false;
        }

        bool isAllowedTopLevelPath = string.Equals(segments[0], "img", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[0], "avatars", StringComparison.OrdinalIgnoreCase);
        bool isAllowedAlbumArtPath = segments.Length >= 3
            && string.Equals(segments[0], "assets", StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[1], "album_art", StringComparison.OrdinalIgnoreCase);

        if (!isAllowedTopLevelPath && !isAllowedAlbumArtPath)
        {
            return false;
        }

        if (segments.Any(segment => string.Equals(segment, ".", StringComparison.Ordinal)
            || string.Equals(segment, "..", StringComparison.Ordinal)
            || segment.Contains(Path.DirectorySeparatorChar)
            || segment.Contains(Path.AltDirectorySeparatorChar)))
        {
            return false;
        }

        normalizedPath = string.Join('/', segments);
        cacheFilePath = Path.Combine(_cacheRootPath, Path.Combine(segments));
        return true;
    }

    private static string GetContentType(string path)
    {
        string extension = Path.GetExtension(path);
        return ContentTypes.TryGetValue(extension, out string? contentType)
            ? contentType
            : "application/octet-stream";
    }
}