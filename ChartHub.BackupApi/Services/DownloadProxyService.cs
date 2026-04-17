using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using ChartHub.BackupApi.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.BackupApi.Services;

public sealed partial class DownloadProxyService : IDownloadProxyService
{
    private static readonly Regex MediaFireDownloadButtonPattern = new("<a[^>]+id=[\"']downloadButton[\"'][^>]+href=[\"'](?<href>[^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".zip"] = "application/zip",
        [".rar"] = "application/vnd.rar",
        [".7z"] = "application/x-7z-compressed",
        [".gz"] = "application/gzip",
        [".tar"] = "application/x-tar",
        [".bz2"] = "application/x-bzip2",
        [".sng"] = "application/octet-stream",
        [".mid"] = "audio/midi",
        [".midi"] = "audio/midi",
        [".chart"] = "text/plain",
        [".con"] = "application/octet-stream",
        [".dta"] = "text/plain",
        [".mogg"] = "audio/ogg",
    };

    private readonly string _cacheRootPath;
    private readonly Uri _upstreamBaseUri;
    private readonly HttpClient _httpClient;
    private readonly IDnsResolver _dnsResolver;
    private readonly ILogger<DownloadProxyService> _logger;
    private readonly int _externalRedirectCacheHours;

    public DownloadProxyService(
        HttpClient httpClient,
        IOptions<DownloadOptions> downloadOptions,
        IOptions<RhythmVerseSourceOptions> sourceOptions,
        IHostEnvironment hostEnvironment,
        IDnsResolver dnsResolver,
        ILogger<DownloadProxyService> logger)
    {
        ArgumentNullException.ThrowIfNull(downloadOptions);
        ArgumentNullException.ThrowIfNull(sourceOptions);
        ArgumentNullException.ThrowIfNull(hostEnvironment);

        _httpClient = httpClient;
        _dnsResolver = dnsResolver;
        _logger = logger;

        string cacheDirectory = downloadOptions.Value.CacheDirectory;
        _cacheRootPath = Path.IsPathRooted(cacheDirectory)
            ? cacheDirectory
            : Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath, cacheDirectory));

        _upstreamBaseUri = new Uri(sourceOptions.Value.BaseUrl, UriKind.Absolute);
        _externalRedirectCacheHours = Math.Max(1, downloadOptions.Value.ExternalRedirectCacheHours);
    }

    public async Task<DownloadProxyResult?> GetDownloadFileAsync(string downloadPath, CancellationToken cancellationToken)
    {
        if (!TryNormalizePath(downloadPath, out string? normalizedPath, out string? cacheFilePath)
            || normalizedPath is null
            || cacheFilePath is null)
        {
            return null;
        }

        if (File.Exists(cacheFilePath))
        {
            return new DownloadProxyResult(cacheFilePath, GetContentType(cacheFilePath));
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

            await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            string? directoryPath = Path.GetDirectoryName(cacheFilePath);
            if (directoryPath is null)
            {
                return null;
            }

            Directory.CreateDirectory(directoryPath);

            await using (FileStream fileStream = new(cacheFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                await responseStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            return new DownloadProxyResult(cacheFilePath, GetContentType(cacheFilePath));
        }
        catch (HttpRequestException ex)
        {
            LogUpstreamRequestThrew(_logger, normalizedPath, ex);
            return null;
        }
    }

    public async Task<DownloadProxyResult?> GetExternalDownloadAsync(string sourceUrl, CancellationToken cancellationToken)
    {
        if (!TryNormalizeExternalUrl(sourceUrl, out Uri? sourceUri) || sourceUri is null)
        {
            return null;
        }

        if (!await ValidateExternalUriAgainstSsrfAsync(sourceUri, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string cacheKey = ComputeCacheKey(sourceUri.ToString());
        ExternalDownloadCacheEntry? cacheEntry = await ReadExternalCacheEntryAsync(cacheKey, cancellationToken).ConfigureAwait(false);

        if (cacheEntry is not null)
        {
            string existingFilePath = GetExternalFilePath(cacheEntry.FileName);
            if (File.Exists(existingFilePath))
            {
                return new DownloadProxyResult(existingFilePath, cacheEntry.ContentType);
            }
        }

        Uri requestUri = sourceUri;
        if (cacheEntry is not null
            && cacheEntry.RedirectCacheExpiresUtc > DateTimeOffset.UtcNow
            && Uri.TryCreate(cacheEntry.ResolvedUrl, UriKind.Absolute, out Uri? cachedResolvedUri))
        {
            requestUri = cachedResolvedUri;
        }
        else if (IsMediaFireUri(sourceUri))
        {
            Uri? resolvedMediaFireUri = await ResolveMediaFireDownloadUriAsync(sourceUri, cancellationToken).ConfigureAwait(false);
            if (resolvedMediaFireUri is null)
            {
                return null;
            }

            if (!await ValidateExternalUriAgainstSsrfAsync(resolvedMediaFireUri, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            requestUri = resolvedMediaFireUri;
        }

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
                LogUpstreamRequestFailed(_logger, requestUri.ToString(), (int)response.StatusCode);
                return null;
            }

            Uri finalUri = response.RequestMessage?.RequestUri ?? requestUri;
            string fileName = BuildExternalCacheFileName(cacheKey, finalUri);
            string filePath = GetExternalFilePath(fileName);
            string contentType = response.Content.Headers.ContentType?.MediaType ?? GetContentType(filePath);

            string? directoryPath = Path.GetDirectoryName(filePath);
            if (directoryPath is null)
            {
                return null;
            }

            Directory.CreateDirectory(directoryPath);

            await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                await responseStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            ExternalDownloadCacheEntry updatedEntry = new()
            {
                SourceUrl = sourceUri.ToString(),
                ResolvedUrl = finalUri.ToString(),
                FileName = fileName,
                ContentType = contentType,
                RedirectCacheExpiresUtc = DateTimeOffset.UtcNow.AddHours(_externalRedirectCacheHours),
            };

            await WriteExternalCacheEntryAsync(cacheKey, updatedEntry, cancellationToken).ConfigureAwait(false);
            return new DownloadProxyResult(filePath, contentType);
        }
        catch (HttpRequestException ex)
        {
            LogUpstreamRequestThrew(_logger, requestUri.ToString(), ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Download proxy upstream request failed for {DownloadPath} with status code {StatusCode}")]
    private static partial void LogUpstreamRequestFailed(ILogger logger, string downloadPath, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Download proxy upstream request threw for {DownloadPath}")]
    private static partial void LogUpstreamRequestThrew(ILogger logger, string downloadPath, Exception exception);

    private bool TryNormalizePath(string downloadPath, out string? normalizedPath, out string? cacheFilePath)
    {
        normalizedPath = null;
        cacheFilePath = null;

        if (string.IsNullOrWhiteSpace(downloadPath))
        {
            return false;
        }

        string trimmedPath = downloadPath.Trim();
        if (Uri.TryCreate(trimmedPath, UriKind.Absolute, out _))
        {
            return false;
        }

        string[] segments = trimmedPath
            .Replace('\\', '/')
            .TrimStart('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2
            || !string.Equals(segments[0], "download_file", StringComparison.OrdinalIgnoreCase))
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

    private async Task<Uri?> ResolveMediaFireDownloadUriAsync(Uri sourceUri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(sourceUri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            LogUpstreamRequestFailed(_logger, sourceUri.ToString(), (int)response.StatusCode);
            return null;
        }

        string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Match match = MediaFireDownloadButtonPattern.Match(content);
        if (!match.Success)
        {
            return null;
        }

        string href = WebUtility.HtmlDecode(match.Groups["href"].Value);
        return Uri.TryCreate(href, UriKind.Absolute, out Uri? resolvedUri)
            ? resolvedUri
            : new Uri(sourceUri, href);
    }

    private async Task<bool> ValidateExternalUriAgainstSsrfAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            System.Net.IPAddress[] addresses = await _dnsResolver.GetHostAddressesAsync(uri.Host, cancellationToken).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                return false;
            }

            foreach (System.Net.IPAddress address in addresses)
            {
                if (PrivateIpBlocker.IsPrivateOrReserved(address))
                {
                    LogSsrfBlocked(_logger, uri.Host);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "SSRF check blocked request to host {Host} resolving to a private/reserved IP")]
    private static partial void LogSsrfBlocked(ILogger logger, string host);

    private static bool TryNormalizeExternalUrl(string sourceUrl, out Uri? normalizedUri)
    {
        normalizedUri = null;

        if (string.IsNullOrWhiteSpace(sourceUrl)
            || !Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out Uri? candidate)
            || (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
            || candidate.IsLoopback
            || candidate.Host.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedUri = candidate;
        return true;
    }

    private static bool IsMediaFireUri(Uri uri)
        => uri.Host.Contains("mediafire.com", StringComparison.OrdinalIgnoreCase);

    private static string ComputeCacheKey(string sourceUrl)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string GetExternalMetadataPath(string cacheKey)
        => Path.Combine(_cacheRootPath, "external", "metadata", cacheKey + ".json");

    private string GetExternalFilePath(string fileName)
        => Path.Combine(_cacheRootPath, "external", "files", fileName);

    private static string BuildExternalCacheFileName(string cacheKey, Uri resolvedUri)
    {
        string extension = Path.GetExtension(resolvedUri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        return cacheKey + extension.ToLowerInvariant();
    }

    private async Task<ExternalDownloadCacheEntry?> ReadExternalCacheEntryAsync(string cacheKey, CancellationToken cancellationToken)
    {
        string metadataPath = GetExternalMetadataPath(cacheKey);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        await using FileStream stream = new(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<ExternalDownloadCacheEntry>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteExternalCacheEntryAsync(string cacheKey, ExternalDownloadCacheEntry entry, CancellationToken cancellationToken)
    {
        string metadataPath = GetExternalMetadataPath(cacheKey);
        string? directoryPath = Path.GetDirectoryName(metadataPath);
        if (directoryPath is null)
        {
            return;
        }

        Directory.CreateDirectory(directoryPath);
        await using FileStream stream = new(metadataPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, entry, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private sealed class ExternalDownloadCacheEntry
    {
        public string SourceUrl { get; set; } = string.Empty;

        public string ResolvedUrl { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = "application/octet-stream";

        public DateTimeOffset RedirectCacheExpiresUtc { get; set; }
    }
}