using System.Net;
using System.Net.Http;
using System.Text;

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

    [Fact]
    public async Task StreamDownloadJobsAsync_ParsesJobsEventPayload()
    {
        HttpRequestMessage? captured = null;
        string ssePayload = "event: jobs\n"
            + "data: [{\"jobId\":\"33333333-3333-3333-3333-333333333333\",\"stage\":\"Downloading\",\"progressPercent\":42.5,\"updatedAtUtc\":\"2026-04-10T00:00:00Z\"}]\n\n";

        ChartHubServerApiClient sut = new(() =>
            new HttpClient(new StubHttpMessageHandler((request, _) =>
            {
                captured = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream"),
                });
            })));

        IReadOnlyList<ChartHubServerDownloadProgressEvent>? batch = null;
        await foreach (IReadOnlyList<ChartHubServerDownloadProgressEvent> item in sut.StreamDownloadJobsAsync("http://127.0.0.1:5001", "jwt-token"))
        {
            batch = item;
            break;
        }

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("http://127.0.0.1:5001/api/v1/downloads/jobs/stream", captured.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("jwt-token", captured.Headers.Authorization?.Parameter);
        Assert.NotNull(batch);
        ChartHubServerDownloadProgressEvent evt = Assert.Single(batch!);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), evt.JobId);
        Assert.Equal("Downloading", evt.Stage);
        Assert.Equal(42.5, evt.ProgressPercent);
    }

    [Fact]
    public async Task ExchangeGoogleTokenAsync_InvalidTokenResponse_ThrowsTypedApiExceptionWithErrorCode()
    {
        ChartHubServerApiClient sut = new(() =>
            new HttpClient(new StubHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":\"invalid_google_id_token\"}"),
                }))));

        ChartHubServerApiException ex = await Assert.ThrowsAsync<ChartHubServerApiException>(() =>
            sut.ExchangeGoogleTokenAsync("http://127.0.0.1:5001", "token"));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("invalid_google_id_token", ex.ErrorCode);
    }

    [Fact]
    public async Task StreamDownloadJobsAsync_Unauthorized_ThrowsTypedApiException()
    {
        ChartHubServerApiClient sut = new(() =>
            new HttpClient(new StubHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("unauthorized"),
                }))));

        ChartHubServerApiException ex = await Assert.ThrowsAsync<ChartHubServerApiException>(async () =>
        {
            await foreach (IReadOnlyList<ChartHubServerDownloadProgressEvent> _ in sut.StreamDownloadJobsAsync("http://127.0.0.1:5001", "bad-token"))
            {
            }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Null(ex.ErrorCode);
    }

    [Fact]
    public async Task StreamVolumeAsync_ParsesVolumeEventPayload()
    {
        HttpRequestMessage? captured = null;
        string ssePayload = "event: volume\n"
            + "data: {\"updatedAtUtc\":\"2026-04-13T00:00:00Z\",\"state\":{\"updatedAtUtc\":\"2026-04-13T00:00:00Z\",\"master\":{\"valuePercent\":35,\"isMuted\":false},\"sessions\":[{\"sessionId\":\"42\",\"name\":\"RetroArch\",\"processId\":1234,\"applicationName\":\"RetroArch\",\"valuePercent\":50,\"isMuted\":false}],\"supportsPerApplicationSessions\":true,\"sessionSupportMessage\":null}}\n\n";

        ChartHubServerApiClient sut = new(() =>
            new HttpClient(new StubHttpMessageHandler((request, _) =>
            {
                captured = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream"),
                });
            })));

        ChartHubServerVolumeStateResponse? state = null;
        await foreach (ChartHubServerVolumeStateResponse item in sut.StreamVolumeAsync("http://127.0.0.1:5001", "jwt-token"))
        {
            state = item;
            break;
        }

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("http://127.0.0.1:5001/api/v1/volume/stream", captured.RequestUri!.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("jwt-token", captured.Headers.Authorization?.Parameter);
        Assert.NotNull(state);
        Assert.Equal(35, state!.Master.ValuePercent);
        ChartHubServerVolumeSessionResponse session = Assert.Single(state.Sessions);
        Assert.Equal("42", session.SessionId);
        Assert.Equal("RetroArch", session.Name);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => sendAsync(request, cancellationToken);
    }
}
