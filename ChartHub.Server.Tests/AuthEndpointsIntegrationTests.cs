using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;

using ChartHub.Server.Contracts;
using ChartHub.Server.Endpoints;
using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Google.Apis.Auth;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Tests;

public sealed class AuthEndpointsIntegrationTests
{
    [Fact]
    public async Task ExchangeWithoutTokenReturnsBadRequest()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync((_, _) =>
            Task.FromResult(new GoogleUserIdentity("allowed@test.local")));

        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync("/api/v1/auth/exchange", new AuthExchangeRequest
        {
            GoogleIdToken = string.Empty,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExchangeWithInvalidTokenReturnsBadRequest()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync((_, _) =>
            throw new InvalidJwtException("invalid"));

        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync("/api/v1/auth/exchange", new AuthExchangeRequest
        {
            GoogleIdToken = "bad-token",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExchangeWithMissingEmailClaimReturnsBadRequest()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync((_, _) =>
            throw new InvalidOperationException("missing email"));

        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync("/api/v1/auth/exchange", new AuthExchangeRequest
        {
            GoogleIdToken = "token",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExchangeWithUpstreamFailureReturnsServiceUnavailable()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync((_, _) =>
            throw new HttpRequestException("google unavailable"));

        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync("/api/v1/auth/exchange", new AuthExchangeRequest
        {
            GoogleIdToken = "token",
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ExchangeWithUnallowlistedEmailReturnsForbidden()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync((_, _) =>
            Task.FromResult(new GoogleUserIdentity("blocked@test.local")));

        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync("/api/v1/auth/exchange", new AuthExchangeRequest
        {
            GoogleIdToken = "token",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExchangeWithAllowlistedEmailReturnsAccessToken()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync((_, _) =>
            Task.FromResult(new GoogleUserIdentity("allowed@test.local")));

        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync("/api/v1/auth/exchange", new AuthExchangeRequest
        {
            GoogleIdToken = "token",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AuthExchangeResponse? payload = await response.Content.ReadFromJsonAsync<AuthExchangeResponse>();

        Assert.NotNull(payload);
        Assert.Equal("test-jwt", payload!.AccessToken);
        Assert.True(payload.ExpiresAtUtc > DateTimeOffset.UtcNow);
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

        public static async Task<TestAppFixture> CreateAsync(Func<string, CancellationToken, Task<GoogleUserIdentity>> validateAsync)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();

            builder.Services
                .AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton<IGoogleIdTokenValidator>(new FakeGoogleIdTokenValidator(validateAsync));
            builder.Services.AddSingleton<IJwtTokenIssuer>(new FakeJwtTokenIssuer());
            builder.Services.AddSingleton<IOptions<AuthOptions>>(Microsoft.Extensions.Options.Options.Create(new AuthOptions
            {
                AllowedEmails = ["allowed@test.local"],
                AccessTokenMinutes = 15,
            }));

            WebApplication app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapAuthEndpoints();

            await app.StartAsync();

            return new TestAppFixture(app, app.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class FakeGoogleIdTokenValidator(Func<string, CancellationToken, Task<GoogleUserIdentity>> validateAsync)
        : IGoogleIdTokenValidator
    {
        public Task<GoogleUserIdentity> ValidateAsync(string idToken, CancellationToken cancellationToken)
            => validateAsync(idToken, cancellationToken);
    }

    private sealed class FakeJwtTokenIssuer : IJwtTokenIssuer
    {
        public string CreateAccessToken(string email, DateTimeOffset expiresAtUtc) => "test-jwt";
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());
    }
}
