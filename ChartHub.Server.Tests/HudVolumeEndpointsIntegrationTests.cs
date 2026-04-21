using System.Net;
using System.Text.Json;

using ChartHub.Server.Contracts;
using ChartHub.Server.Endpoints;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ChartHub.Server.Tests;

public sealed class HudVolumeEndpointsIntegrationTests
{
    [Fact]
    public async Task StreamReturnsForbiddenForNonLoopbackCaller()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/hud/volume/stream");
        request.Headers.Add("X-Test-Remote-Ip", "10.0.0.25");

        HttpResponseMessage response = await fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StreamReturnsHudVolumeEventForLoopbackCaller()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/v1/hud/volume/stream");
        request.Headers.Add("X-Test-Remote-Ip", "127.0.0.1");

        using HttpResponseMessage response = await fixture.Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);

        response.EnsureSuccessStatusCode();
        Assert.Contains("text/event-stream", response.Content.Headers.ContentType?.ToString(), StringComparison.OrdinalIgnoreCase);

        await using Stream stream = await response.Content.ReadAsStreamAsync();
        using StreamReader reader = new(stream);

        string? eventLine = await reader.ReadLineAsync();
        string? dataLine = await reader.ReadLineAsync();

        Assert.Equal("event: hud-volume", eventLine);
        Assert.NotNull(dataLine);
        Assert.StartsWith("data: ", dataLine, StringComparison.Ordinal);

        string payloadJson = dataLine!["data: ".Length..];
        HudVolumePayload? payload = JsonSerializer.Deserialize<HudVolumePayload>(
            payloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(payload);
        Assert.True(payload!.IsAvailable);
        Assert.Equal(64, payload.ValuePercent);
        Assert.False(payload.IsMuted);
    }

    private sealed class TestAppFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TestAppFixture(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<TestAppFixture> CreateAsync()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();

            builder.Services.AddSingleton<IVolumeService, FakeVolumeService>();

            WebApplication app = builder.Build();

            app.Use((context, next) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Remote-Ip", out Microsoft.Extensions.Primitives.StringValues value)
                    && IPAddress.TryParse(value.ToString(), out IPAddress? remoteIp))
                {
                    context.Connection.RemoteIpAddress = remoteIp;
                }

                return next(context);
            });

            app.MapHudVolumeEndpoints();

            await app.StartAsync();

            HttpClient client = app.GetTestClient();
            return new TestAppFixture(app, client);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class FakeVolumeService : IVolumeService
    {
        public bool IsSupportedPlatform => true;

        public int SseHeartbeatSeconds => 2;

        public long CurrentChangeStamp => 0;

        public Task<VolumeStateResponse> GetStateAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new VolumeStateResponse
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Master = new VolumeMasterStateResponse
                {
                    ValuePercent = 64,
                    IsMuted = false,
                },
                SupportsMasterVolume = true,
                SupportsPerApplicationSessions = true,
                Sessions = [],
            });
        }

        public Task<VolumeActionResponse> SetMasterVolumeAsync(int valuePercent, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<VolumeActionResponse> SetSessionVolumeAsync(string sessionId, int valuePercent, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> WaitForChangeAsync(long observedChangeStamp, TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class HudVolumePayload
    {
        public bool IsAvailable { get; init; }
        public int ValuePercent { get; init; }
        public bool IsMuted { get; init; }
    }
}