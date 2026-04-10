using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using ChartHub.Utilities;

namespace ChartHub.Services;

public interface IChartHubServerApiClient
{
    Task<ChartHubServerAuthExchangeResponse> ExchangeGoogleTokenAsync(string baseUrl, string googleIdToken, CancellationToken cancellationToken = default);

    Task<ChartHubServerDownloadJobResponse> CreateDownloadJobAsync(string baseUrl, string bearerToken, ChartHubServerCreateDownloadJobRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChartHubServerDownloadJobResponse>> ListDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default);

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

public sealed class ChartHubServerApiClient : IChartHubServerApiClient
{
    private readonly Func<HttpClient> _createClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ChartHubServerApiClient()
        : this(() =>
        {
            HttpClient client = new();
            client.Timeout = TimeSpan.FromSeconds(30);
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
            throw new InvalidOperationException($"ChartHub.Server request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {responseBody}");
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

    private sealed class AuthExchangeEnvelope
    {
        public string? AccessToken { get; init; }

        public DateTimeOffset ExpiresAtUtc { get; init; }
    }
}
