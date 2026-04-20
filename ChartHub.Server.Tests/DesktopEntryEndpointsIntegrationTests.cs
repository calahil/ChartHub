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

public sealed class DesktopEntryEndpointsIntegrationTests
{
    [Fact]
    public async Task DesktopEntryListRequiresAuth()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(authenticatedClient: false);

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/desktopentries");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DesktopEntryListReturnsItems()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/desktopentries");
        response.EnsureSuccessStatusCode();

        List<DesktopEntryItemResponse>? payload = await response.Content.ReadFromJsonAsync<List<DesktopEntryItemResponse>>();
        Assert.NotNull(payload);
        DesktopEntryItemResponse item = Assert.Single(payload);
        Assert.Equal("retro", item.EntryId);
        Assert.Equal("Not running", item.Status);
    }

    [Fact]
    public async Task ExecuteReturnsConflictWhenAlreadyRunning()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(service =>
        {
            service.ExecuteException = new DesktopEntryServiceException(StatusCodes.Status409Conflict, "already_running", "already running");
        });

        HttpResponseMessage response = await fixture.Client.PostAsync("/api/v1/desktopentries/retro/execute", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task IconEndpointReturnsIconWhenAuthenticated()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(authenticatedClient: true);

        HttpResponseMessage response = await fixture.Client.GetAsync("/desktopentry-icons/retro/retro.png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task IconEndpointReturnsIconWhenUnauthenticated()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(authenticatedClient: false);

        HttpResponseMessage response = await fixture.Client.GetAsync("/desktopentry-icons/retro/retro.png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListReturnsNotImplementedWhenServiceUnsupported()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(service =>
        {
            service.ListException = new DesktopEntryServiceException(StatusCodes.Status501NotImplemented, "unsupported_platform", "unsupported");
        });

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/desktopentries");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    private sealed class TestAppFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _tempDirectory;

        private TestAppFixture(WebApplication app, HttpClient client, string tempDirectory)
        {
            _app = app;
            Client = client;
            _tempDirectory = tempDirectory;
        }

        public HttpClient Client { get; }

        public static async Task<TestAppFixture> CreateAsync(
            Action<FakeDesktopEntryService>? configureService = null,
            bool authenticatedClient = true)
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "charthub-desktopentry-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            string iconPath = Path.Combine(tempDirectory, "retro.png");
            await File.WriteAllBytesAsync(iconPath, [0x89, 0x50, 0x4E, 0x47]);

            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();

            builder.Services
                .AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            builder.Services.AddAuthorization();

            var desktopEntryService = new FakeDesktopEntryService(iconPath);
            configureService?.Invoke(desktopEntryService);
            builder.Services.AddSingleton<IDesktopEntryService>(desktopEntryService);

            WebApplication app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapDesktopEntryEndpoints();

            await app.StartAsync();

            HttpClient client = app.GetTestClient();
            if (authenticatedClient)
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
            }

            return new TestAppFixture(app, client, tempDirectory);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
    }

    private sealed class FakeDesktopEntryService(string iconPath) : IDesktopEntryService
    {
        public bool IsEnabled => true;

        public bool IsSupportedPlatform => true;

        public int SseIntervalSeconds => 2;

        public Exception? ListException { get; set; }

        public Exception? ExecuteException { get; set; }

        public Task RefreshCatalogAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<DesktopEntryItemResponse>> ListEntriesAsync(CancellationToken cancellationToken)
        {
            if (ListException is not null)
            {
                throw ListException;
            }

            return Task.FromResult<IReadOnlyList<DesktopEntryItemResponse>>(
            [
                new DesktopEntryItemResponse
                {
                    EntryId = "retro",
                    Name = "RetroArch",
                    Status = "Not running",
                    ProcessId = null,
                    IconUrl = "/desktopentry-icons/retro/retro.png",
                },
            ]);
        }

        public Task<DesktopEntryActionResponse> ExecuteAsync(string entryId, CancellationToken cancellationToken)
        {
            if (ExecuteException is not null)
            {
                throw ExecuteException;
            }

            return Task.FromResult(new DesktopEntryActionResponse
            {
                EntryId = entryId,
                Status = "Running",
                ProcessId = 100,
                Message = "started",
            });
        }

        public Task<DesktopEntryActionResponse> KillAsync(string entryId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DesktopEntryActionResponse
            {
                EntryId = entryId,
                Status = "Not running",
                ProcessId = null,
                Message = "killed",
            });
        }

        public bool TryResolveIconFile(string entryId, string fileName, out string iconPathResult, out string contentType)
        {
            if (entryId == "retro" && fileName == "retro.png")
            {
                iconPathResult = iconPath;
                contentType = "image/png";
                return true;
            }

            iconPathResult = string.Empty;
            contentType = string.Empty;
            return false;
        }
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
