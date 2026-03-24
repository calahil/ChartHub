using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using ChartHub.Models;

namespace ChartHub.Services;

public interface IDesktopSyncApiClient
{
    Task<DesktopSyncPairClaimResponse> ClaimPairTokenAsync(string baseUrl, string pairCode, string? deviceLabel = null, CancellationToken cancellationToken = default);
    Task<DesktopSyncVersionResponse> GetVersionAsync(string baseUrl, string token, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IngestionQueueItem>> GetIngestionsAsync(string baseUrl, string token, int limit = 100, CancellationToken cancellationToken = default);
    Task<long> UploadIngestionFileAsync(
        string baseUrl,
        string token,
        string localPath,
        string displayName,
        LocalIngestionUploadMetadata? metadata = null,
        CancellationToken cancellationToken = default);
    Task TriggerRetryAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default);
    Task TriggerInstallAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default);
    Task TriggerOpenFolderAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default);
}

public sealed record DesktopSyncVersionResponse(string Api, string Version, bool SupportsIngestions, bool SupportsDesktopState);
public sealed record DesktopSyncPairClaimResponse(bool Paired, string Token, string ApiBaseUrl);
public sealed record LocalIngestionUploadMetadata(
    string Source,
    string? SourceId,
    string? SourceLink,
    string? Artist,
    string? Title,
    string? Charter,
    string? LibrarySource);

public sealed class DesktopSyncApiClient : IDesktopSyncApiClient
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ActionRequestTimeout = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<DesktopSyncPairClaimResponse> ClaimPairTokenAsync(string baseUrl, string pairCode, string? deviceLabel = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pairCode))
        {
            throw new InvalidOperationException("Pair code is required.");
        }

        string payload = JsonSerializer.Serialize(new
        {
            pairCode,
            deviceLabel,
        });

        using HttpRequestMessage request = BuildRequest(HttpMethod.Post, baseUrl, "/api/pair/claim", token: string.Empty);
        request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        PairClaimEnvelope claim = await ReadJsonAsync<PairClaimEnvelope>(response, cancellationToken).ConfigureAwait(false);

        return new DesktopSyncPairClaimResponse(
            Paired: claim.Paired,
            Token: claim.Token ?? string.Empty,
            ApiBaseUrl: claim.ApiBaseUrl ?? string.Empty);
    }

    public async Task<DesktopSyncVersionResponse> GetVersionAsync(string baseUrl, string token, CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = BuildRequest(HttpMethod.Get, baseUrl, "/api/version", token);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        VersionEnvelope payload = await ReadJsonAsync<VersionEnvelope>(response, cancellationToken).ConfigureAwait(false);

        return new DesktopSyncVersionResponse(
            Api: payload.Api ?? "",
            Version: payload.Version ?? "",
            SupportsIngestions: payload.Supports?.Ingestions ?? false,
            SupportsDesktopState: payload.Supports?.DesktopState ?? false);
    }

    public async Task<IReadOnlyList<IngestionQueueItem>> GetIngestionsAsync(string baseUrl, string token, int limit = 100, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        using HttpRequestMessage request = BuildRequest(HttpMethod.Get, baseUrl, $"/api/ingestions?limit={limit}", token);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        QueueEnvelope payload = await ReadJsonAsync<QueueEnvelope>(response, cancellationToken).ConfigureAwait(false);

        return payload.Items?.Select(MapQueueItem).ToList() ?? [];
    }

    public async Task<long> UploadIngestionFileAsync(
        string baseUrl,
        string token,
        string localPath,
        string displayName,
        LocalIngestionUploadMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            throw new InvalidOperationException("Local file path is required.");
        }

        if (!File.Exists(localPath))
        {
            throw new InvalidOperationException("Local file does not exist.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException("Display name is required.");
        }

        await using FileStream fileStream = File.OpenRead(localPath);
        using HttpRequestMessage request = BuildRequest(HttpMethod.Post, baseUrl, "/api/ingestions/upload", token);
        using var formData = new MultipartFormDataContent();

        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        formData.Add(fileContent, "file", displayName);
        formData.Add(new StringContent(displayName), "displayName");

        if (!string.IsNullOrWhiteSpace(metadata?.Source))
        {
            formData.Add(new StringContent(metadata.Source), "source");
        }

        if (!string.IsNullOrWhiteSpace(metadata?.SourceId))
        {
            formData.Add(new StringContent(metadata.SourceId), "sourceId");
        }

        if (!string.IsNullOrWhiteSpace(metadata?.SourceLink))
        {
            formData.Add(new StringContent(metadata.SourceLink), "sourceLink");
        }

        if (!string.IsNullOrWhiteSpace(metadata?.Artist))
        {
            formData.Add(new StringContent(metadata.Artist), "artist");
        }

        if (!string.IsNullOrWhiteSpace(metadata?.Title))
        {
            formData.Add(new StringContent(metadata.Title), "title");
        }

        if (!string.IsNullOrWhiteSpace(metadata?.Charter))
        {
            formData.Add(new StringContent(metadata.Charter), "charter");
        }

        if (!string.IsNullOrWhiteSpace(metadata?.LibrarySource))
        {
            formData.Add(new StringContent(metadata.LibrarySource), "librarySource");
        }

        request.Content = formData;

        using HttpResponseMessage response = await SendAsync(request, cancellationToken, ActionRequestTimeout).ConfigureAwait(false);
        UploadEnvelope payload = await ReadJsonAsync<UploadEnvelope>(response, cancellationToken).ConfigureAwait(false);

        return payload.IngestionId;
    }

    public Task TriggerRetryAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default)
    {
        return PostActionAsync(baseUrl, token, ingestionId, "retry", cancellationToken);
    }

    public Task TriggerInstallAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default)
    {
        return PostActionAsync(baseUrl, token, ingestionId, "install", cancellationToken);
    }

    public Task TriggerOpenFolderAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default)
    {
        return PostActionAsync(baseUrl, token, ingestionId, "open-folder", cancellationToken);
    }

    private static async Task PostActionAsync(string baseUrl, string token, long ingestionId, string action, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = BuildRequest(HttpMethod.Post, baseUrl, $"/api/ingestions/{ingestionId}/actions/{action}", token);
        using HttpResponseMessage _ = await SendAsync(request, cancellationToken, ActionRequestTimeout).ConfigureAwait(false);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string baseUrl, string relativePath, string token)
    {
        Uri baseUri = ParseBaseUri(baseUrl);
        var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("X-ChartHub-Sync-Token", token);
        }

        return request;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        using var httpClient = new HttpClient
        {
            Timeout = timeout ?? DefaultRequestTimeout,
        };

        HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        string errorBody = await TryReadErrorAsync(response, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException($"Desktop sync API request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {errorBody}");
    }

    private static async Task<string> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "No error payload.";
        }

        try
        {
            ErrorEnvelope? envelope = JsonSerializer.Deserialize<ErrorEnvelope>(payload, JsonOptions);
            if (!string.IsNullOrWhiteSpace(envelope?.Error))
            {
                return envelope.Error;
            }
        }
        catch
        {
            // Fall through and return raw payload.
        }

        return payload;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        T? result = JsonSerializer.Deserialize<T>(payload, JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException("Desktop sync API returned an empty payload.");
        }

        return result;
    }

    private static Uri ParseBaseUri(string baseUrl)
    {
        string candidate = baseUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new InvalidOperationException("Desktop API URL is required.");
        }

        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = $"http://{candidate}";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("Desktop API URL is invalid.");
        }

        return uri;
    }

    private static IngestionQueueItem MapQueueItem(IngestionQueueItemEnvelope item)
    {
        IngestionState state = Enum.TryParse<IngestionState>(item.CurrentState, ignoreCase: true, out IngestionState parsedState)
            ? parsedState
            : default;
        DesktopState desktopState = Enum.TryParse<DesktopState>(item.DesktopState, ignoreCase: true, out DesktopState parsedDesktopState)
            ? parsedDesktopState
            : default;
        DateTimeOffset updatedAtUtc = DateTimeOffset.TryParse(item.UpdatedAtUtc, out DateTimeOffset parsedUpdatedAtUtc)
            ? parsedUpdatedAtUtc
            : default;

        return new IngestionQueueItem
        {
            IngestionId = item.IngestionId,
            Source = item.Source ?? "",
            SourceId = item.SourceId,
            SourceLink = item.SourceLink ?? "",
            Artist = item.Artist,
            Title = item.Title,
            Charter = item.Charter,
            DisplayName = item.DisplayName ?? $"Ingestion {item.IngestionId}",
            CurrentState = state,
            DownloadedLocation = item.DownloadedLocation,
            InstalledLocation = item.InstalledLocation,
            DesktopState = desktopState,
            DesktopLibraryPath = item.DesktopLibraryPath,
            UpdatedAtUtc = updatedAtUtc,
        };
    }

    private sealed class ErrorEnvelope
    {
        public string? Error { get; set; }
    }

    private sealed class VersionEnvelope
    {
        public string? Api { get; set; }
        public string? Version { get; set; }
        public VersionSupportsEnvelope? Supports { get; set; }
    }

    private sealed class VersionSupportsEnvelope
    {
        public bool Ingestions { get; set; }
        public bool DesktopState { get; set; }
    }

    private sealed class QueueEnvelope
    {
        public List<IngestionQueueItemEnvelope>? Items { get; set; }
    }

    private sealed class PairClaimEnvelope
    {
        public bool Paired { get; set; }
        public string? Token { get; set; }
        public string? ApiBaseUrl { get; set; }
    }

    private sealed class UploadEnvelope
    {
        public long IngestionId { get; set; }
    }

    private sealed class IngestionQueueItemEnvelope
    {
        public long IngestionId { get; set; }
        public string? Source { get; set; }
        public string? SourceId { get; set; }
        public string? SourceLink { get; set; }
        public string? Artist { get; set; }
        public string? Title { get; set; }
        public string? Charter { get; set; }
        public string? DisplayName { get; set; }
        public string? CurrentState { get; set; }
        public string? DownloadedLocation { get; set; }
        public string? InstalledLocation { get; set; }
        public string? DesktopState { get; set; }
        public string? DesktopLibraryPath { get; set; }
        public string? UpdatedAtUtc { get; set; }
    }
}