using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;

using ChartHub.Server.Contracts;
using ChartHub.Server.Endpoints;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Tests;

public sealed class VolumeEndpointsIntegrationTests
{
    [Fact]
    public async Task VolumeStateRequiresAuth()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(authenticatedClient: false);

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/volume");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VolumeStateReturnsPayload()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/volume");
        response.EnsureSuccessStatusCode();

        VolumeStateResponse? payload = await response.Content.ReadFromJsonAsync<VolumeStateResponse>();
        Assert.NotNull(payload);
        Assert.Equal(35, payload!.Master.ValuePercent);
        Assert.Single(payload.Sessions);
    }

    [Fact]
    public async Task SetSessionVolumeReturnsNotFoundWhenSessionMissing()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(service =>
        {
            service.SetSessionException = new VolumeServiceException(StatusCodes.Status404NotFound, "session_not_found", "missing");
        });

        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync("/api/v1/volume/sessions/missing", new SetVolumeRequest { ValuePercent = 20 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

        public static async Task<TestAppFixture> CreateAsync(
            Action<FakeVolumeService>? configureService = null,
            bool authenticatedClient = true)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();

            builder.Services
                .AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            builder.Services.AddAuthorization();

            FakeVolumeService volumeService = new();
            configureService?.Invoke(volumeService);
            builder.Services.AddSingleton<IVolumeService>(volumeService);

            WebApplication app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapVolumeEndpoints();

            await app.StartAsync();

            HttpClient client = app.GetTestClient();
            if (authenticatedClient)
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
            }

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

        public Exception? SetSessionException { get; set; }

        public Task<VolumeStateResponse> GetStateAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new VolumeStateResponse
            {
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Master = new VolumeMasterStateResponse
                {
                    ValuePercent = 35,
                    IsMuted = false,
                },
                Sessions =
                [
                    new VolumeSessionResponse
                    {
                        SessionId = "42",
                        Name = "RetroArch",
                        ProcessId = 1234,
                        ApplicationName = "RetroArch",
                        ValuePercent = 55,
                        IsMuted = false,
                    },
                ],
                SupportsPerApplicationSessions = true,
            });
        }

        public Task<VolumeActionResponse> SetMasterVolumeAsync(int valuePercent, CancellationToken cancellationToken)
        {
            return Task.FromResult(new VolumeActionResponse
            {
                TargetId = "master",
                TargetKind = "master",
                Name = "Master Volume",
                ValuePercent = valuePercent,
                IsMuted = false,
                Message = "updated",
            });
        }

        public Task<VolumeActionResponse> SetSessionVolumeAsync(string sessionId, int valuePercent, CancellationToken cancellationToken)
        {
            if (SetSessionException is not null)
            {
                throw SetSessionException;
            }

            return Task.FromResult(new VolumeActionResponse
            {
                TargetId = sessionId,
                TargetKind = "session",
                Name = "RetroArch",
                ValuePercent = valuePercent,
                IsMuted = false,
                Message = "updated",
            });
        }

        public Task<bool> WaitForChangeAsync(long observedChangeStamp, TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out Microsoft.Extensions.Primitives.StringValues value)
                || value.Count == 0)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, "test-user"),
                new Claim(ClaimTypes.Name, "test-user"),
            ],
                Scheme.Name);

            ClaimsPrincipal principal = new(identity);
            AuthenticationTicket ticket = new(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}