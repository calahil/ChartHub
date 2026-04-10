using System.Net;
using System.Net.Http;

using ChartHub.Services;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public sealed class ChartHubServerApiClientTests
{
    [Fact]
    public async Task CreateDownloadJobAsyncBuildsExpectedRequestAndParsesResponse()
    {
        HttpRequestMessage? captured = null;
        ChartHubServerApiClient sut = new(() =>
            new HttpClient(new StubHttpMessageHandler(async (request, _) =>
            {
                captured = request;
                string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
                Assert.Contains("\"source\":\"rhythmverse\"", body, StringComparison.OrdinalIgnoreCase);
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"jobId\":\"5f968c0e-6fd4-4954-8df7-f70f6b2ca6c2\",\"source\":\"rhythmverse\",\"sourceId\":\"src-1\",\"displayName\":\"Track\",\"sourceUrl\":\"https://example.com/track.zip\",\"stage\":\"Queued\",\"progressPercent\":0,\"createdAtUtc\":\"2026-04-10T00:00:00Z\",\"updatedAtUtc\":\"2026-04-10T00:00:00Z\"}"),
                };
            })));

        ChartHubServerDownloadJobResponse result = await sut.CreateDownloadJobAsync(
            "http://127.0.0.1:5001",
            "jwt-token",
            new ChartHubServerCreateDownloadJobRequest("rhythmverse", "src-1", "Track", "https://example.com/track.zip"));

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("http://127.0.0.1:5001/api/v1/downloads/jobs", captured.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("jwt-token", captured.Headers.Authorization?.Parameter);
        Assert.Equal(Guid.Parse("5f968c0e-6fd4-4954-8df7-f70f6b2ca6c2"), result.JobId);
        Assert.Equal("Queued", result.Stage);
    }

    [Fact]
    public async Task ExchangeGoogleTokenAsyncThrowsForInvalidBaseUrl()
    {
        ChartHubServerApiClient sut = new(() => new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExchangeGoogleTokenAsync("localhost:5001", "token"));
    }

    [Fact]
    public async Task RequestCancelDownloadJobAsyncBuildsExpectedRequest()
    {
        HttpRequestMessage? captured = null;
        var sut = new ChartHubServerApiClient(() =>
            new HttpClient(new StubHttpMessageHandler((request, _) =>
            {
                captured = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
            })));

        var jobId = Guid.Parse("6e841c5a-2a9a-42f4-a434-486a8c5d1f07");
        await sut.RequestCancelDownloadJobAsync("http://127.0.0.1:5001", "jwt-token", jobId);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"http://127.0.0.1:5001/api/v1/downloads/jobs/{jobId:D}/cancel", captured.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("jwt-token", captured.Headers.Authorization?.Parameter);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => sendAsync(request, cancellationToken);
    }
}
