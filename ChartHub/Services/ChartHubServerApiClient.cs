using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using ChartHub.Utilities;

namespace ChartHub.Services;

public interface IChartHubServerApiClient
{
    Task<ChartHubServerAuthExchangeResponse> ExchangeGoogleTokenAsync(string baseUrl, string googleIdToken, CancellationToken cancellationToken = default);

    Task<ChartHubServerDownloadJobResponse> CreateDownloadJobAsync(string baseUrl, string bearerToken, ChartHubServerCreateDownloadJobRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChartHubServerDownloadJobResponse>> ListDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default);

    IAsyncEnumerable<IReadOnlyList<ChartHubServerDownloadProgressEvent>> StreamDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default);

    Task<ChartHubServerDownloadJobResponse> RequestInstallDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default);

    Task RequestCancelDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default);
}

public sealed record ChartHubServerAuthExchangeResponse(string AccessToken, DateTimeOffset ExpiresAtUtc);

public sealed record ChartHubServerCreateDownloadJobRequest(
    string Source,
    string SourceId,
    string DisplayName,
    string SourceUrl);

public sealed record ChartHubServerDownloadJobResponse(
    Guid JobId,
    string Source,
    string SourceId,
    string DisplayName,
    string SourceUrl,
    string Stage,
    double ProgressPercent,
    string? DownloadedPath,
    string? StagedPath,
    string? InstalledPath,
    string? Error,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ChartHubServerDownloadProgressEvent(
    Guid JobId,
    string Stage,
    double ProgressPercent,
    DateTimeOffset UpdatedAtUtc);

public sealed class ChartHubServerApiException : InvalidOperationException
{
    public ChartHubServerApiException(HttpStatusCode statusCode, string? reasonPhrase, string responseBody, string? errorCode)
        : base($"ChartHub.Server request failed ({(int)statusCode} {reasonPhrase}): {responseBody}")
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        ResponseBody = responseBody;
        ErrorCode = errorCode;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ReasonPhrase { get; }

    public string ResponseBody { get; }

    public string? ErrorCode { get; }
}

public sealed class ChartHubServerApiClient : IChartHubServerApiClient
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(5);
    private readonly Func<HttpClient> _createClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ChartHubServerApiClient()
        : this(() =>
        {
            HttpClient client = new();
            client.Timeout = DefaultRequestTimeout;
            return client;
        })
    {
    }

    public ChartHubServerApiClient(Func<HttpClient> createClient)
    {
        _createClient = createClient;
    }

    public async Task<ChartHubServerAuthExchangeResponse> ExchangeGoogleTokenAsync(
        string baseUrl,
        string googleIdToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(googleIdToken))
        {
            throw new InvalidOperationException("Google ID token is required.");
        }

        using HttpRequestMessage request = BuildRequest(HttpMethod.Post, baseUrl, "/api/v1/auth/exchange", token: null);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { googleIdToken }),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        AuthExchangeEnvelope payload = await ReadJsonAsync<AuthExchangeEnvelope>(response, cancellationToken).ConfigureAwait(false);

        return new ChartHubServerAuthExchangeResponse(
            payload.AccessToken ?? string.Empty,
            payload.ExpiresAtUtc);
    }

    public async Task<ChartHubServerDownloadJobResponse> CreateDownloadJobAsync(
        string baseUrl,
        string bearerToken,
        ChartHubServerCreateDownloadJobRequest request,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage httpRequest = BuildRequest(HttpMethod.Post, baseUrl, "/api/v1/downloads/jobs", bearerToken);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<ChartHubServerDownloadJobResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ChartHubServerDownloadJobResponse>> ListDownloadJobsAsync(
        string baseUrl,
        string bearerToken,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = BuildRequest(HttpMethod.Get, baseUrl, "/api/v1/downloads/jobs", bearerToken);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);

        List<ChartHubServerDownloadJobResponse>? payload = await ReadJsonAsync<List<ChartHubServerDownloadJobResponse>>(response, cancellationToken).ConfigureAwait(false);
        return payload ?? [];
    }

    public async IAsyncEnumerable<IReadOnlyList<ChartHubServerDownloadProgressEvent>> StreamDownloadJobsAsync(
        string baseUrl,
        string bearerToken,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = BuildRequest(HttpMethod.Get, baseUrl, "/api/v1/downloads/jobs/stream", bearerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using HttpClient client = _createClient();
        client.Timeout = Timeout.InfiniteTimeSpan;

        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw CreateRequestFailedException(response.StatusCode, response.ReasonPhrase, responseBody);
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        string eventName = string.Empty;
        string? line;
        var dataBuilder = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }

            if (line.Length == 0)
            {
                if (!string.Equals(eventName, "jobs", StringComparison.OrdinalIgnoreCase)
                    || dataBuilder.Length == 0)
                {
                    eventName = string.Empty;
                    dataBuilder.Clear();
                    continue;
                }

                string payloadJson = dataBuilder.ToString();
                dataBuilder.Clear();
                eventName = string.Empty;

                List<ChartHubServerDownloadProgressEvent>? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<List<ChartHubServerDownloadProgressEvent>>(payloadJson, JsonOptions);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException("ChartHub.Server stream returned invalid jobs payload.", ex);
                }

                yield return payload ?? [];
                continue;
            }

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line["event:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (dataBuilder.Length > 0)
                {
                    dataBuilder.Append('\n');
                }

                dataBuilder.Append(line["data:".Length..].TrimStart());
            }
        }
    }

    public async Task RequestCancelDownloadJobAsync(
        string baseUrl,
        string bearerToken,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = BuildRequest(HttpMethod.Post, baseUrl, $"/api/v1/downloads/jobs/{jobId:D}/cancel", bearerToken);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"ChartHub.Server request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {responseBody}");
        }
    }

    public async Task<ChartHubServerDownloadJobResponse> RequestInstallDownloadJobAsync(
        string baseUrl,
        string bearerToken,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = BuildRequest(HttpMethod.Post, baseUrl, $"/api/v1/downloads/jobs/{jobId:D}/install", bearerToken);
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<ChartHubServerDownloadJobResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string baseUrl, string relativePath, string? token)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Base URL is required.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Base URL must be an absolute HTTP/HTTPS URL.");
        }

        Uri requestUri = new(baseUri, relativePath);
        HttpRequestMessage request = new(method, requestUri);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateRequestFailedException(response.StatusCode, response.ReasonPhrase, responseBody);
        }

        T? payload = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        if (payload is null)
        {
            throw new InvalidOperationException("ChartHub.Server response body was empty or invalid JSON.");
        }

        return payload;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using HttpClient client = _createClient();
        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static ChartHubServerApiException CreateRequestFailedException(HttpStatusCode statusCode, string? reasonPhrase, string responseBody)
    {
        string? errorCode = TryParseErrorCode(responseBody);
        return new ChartHubServerApiException(statusCode, reasonPhrase, responseBody, errorCode);
    }

    private static string? TryParseErrorCode(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out JsonElement errorElement)
                && errorElement.ValueKind == JsonValueKind.String)
            {
                return errorElement.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private sealed class AuthExchangeEnvelope
    {
        public string? AccessToken { get; init; }

        public DateTimeOffset ExpiresAtUtc { get; init; }
    }
}
