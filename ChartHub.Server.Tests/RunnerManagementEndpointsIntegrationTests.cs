using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;

using ChartHub.Server.Contracts;
using ChartHub.Server.Endpoints;
using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Tests;

public sealed class RunnerManagementEndpointsIntegrationTests
{
    private const string ValidKey = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";

    // ── POST /api/v1/runners/registration-tokens ──────────────────────────────

    [Fact]
    public async Task IssueTokenWithValidApiKeyReturnsOk()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(ValidKey, authenticatedClient: false);

        fixture.Client.DefaultRequestHeaders.Add(RunnerManagementEndpoints.ApiKeyHeader, ValidKey);
        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/runners/registration-tokens",
            new IssueRunnerRegistrationTokenRequest { TtlMinutes = 15 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task IssueTokenWithValidUserJwtReturnsOk()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(ValidKey, authenticatedClient: true);

        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/runners/registration-tokens",
            new IssueRunnerRegistrationTokenRequest { TtlMinutes = 15 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task IssueTokenWithWrongApiKeyReturnsUnauthorized()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(ValidKey, authenticatedClient: false);

        fixture.Client.DefaultRequestHeaders.Add(RunnerManagementEndpoints.ApiKeyHeader, "wrong-key");
        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/runners/registration-tokens",
            new IssueRunnerRegistrationTokenRequest { TtlMinutes = 15 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IssueTokenWithNoKeyAndNoJwtReturnsUnauthorized()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(ValidKey, authenticatedClient: false);

        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/runners/registration-tokens",
            new IssueRunnerRegistrationTokenRequest { TtlMinutes = 15 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IssueTokenWithEmptyConfiguredKeyReturnsForbidden()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(configuredKey: "", authenticatedClient: false);

        fixture.Client.DefaultRequestHeaders.Add(RunnerManagementEndpoints.ApiKeyHeader, "any-key");
        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/runners/registration-tokens",
            new IssueRunnerRegistrationTokenRequest { TtlMinutes = 15 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GET /api/v1/runners ───────────────────────────────────────────────────

    [Fact]
    public async Task ListRunnersWithoutAuthReturnsUnauthorized()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(ValidKey, authenticatedClient: false);

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/runners/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListRunnersWithValidJwtReturnsOk()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(ValidKey, authenticatedClient: true);

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/runners/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Fixtures and fakes ────────────────────────────────────────────────────

    private sealed class TestAppFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TestAppFixture(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<TestAppFixture> CreateAsync(string configuredKey, bool authenticatedClient)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();

            builder.Services
                .AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            builder.Services.AddAuthorization();

            builder.Services.Configure<RunnerOptions>(opt => opt.ManagementApiKey = configuredKey);
            builder.Services.AddSingleton<ITranscriptionRunnerRegistry>(new FakeRunnerRegistry());

            WebApplication app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapRunnerManagementEndpoints();

            await app.StartAsync();

            HttpClient client = app.GetTestClient();
            if (authenticatedClient)
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
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

    private sealed class FakeRunnerRegistry : ITranscriptionRunnerRegistry
    {
        public RunnerRegistrationTokenResponse IssueRegistrationToken(TimeSpan ttl) =>
            new()
            {
                TokenId = "test-token-id",
                PlainToken = "test-plain-token",
                ExpiresAtUtc = DateTimeOffset.UtcNow.Add(ttl),
            };

        public RegisterRunnerResponse RegisterRunner(string runnerName, string plainToken, string plainSecret, int maxConcurrency) =>
            throw new NotImplementedException();

        public TranscriptionRunnerRecord? ValidateRunner(string runnerId, string plainSecret) =>
            null;

        public void RecordHeartbeat(string runnerId, int activeJobCount) { }

        public IReadOnlyList<TranscriptionRunnerRecord> ListRunners() => [];

        public bool TryDeregisterRunner(string runnerId) => false;
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out Microsoft.Extensions.Primitives.StringValues value) || value.Count == 0)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, "test-user"),
                new Claim(ClaimTypes.Name, "test-user"),
            ],
                Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
