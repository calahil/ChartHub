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

public sealed class CloneHeroEndpointsIntegrationTests
{
    [Fact]
    public async Task CloneHeroSongsEndpointRequiresAuth()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(authenticatedClient: false);

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/clonehero/songs");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUnknownSongReturnsNotFound()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.DeleteAsync("/api/v1/clonehero/songs/missing-song");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RestoreUnknownSongReturnsNotFound()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.PostAsync("/api/v1/clonehero/songs/missing-song/restore", content: null);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteThenRestoreSongUsesExpectedHttpStatusCodes()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage listResponse = await fixture.Client.GetAsync("/api/v1/clonehero/songs");
        listResponse.EnsureSuccessStatusCode();
        IReadOnlyList<CloneHeroSongResponse>? songs = await listResponse.Content.ReadFromJsonAsync<IReadOnlyList<CloneHeroSongResponse>>();
        Assert.NotNull(songs);
        string songId = Assert.Single(songs).SongId;

        HttpResponseMessage deleteResponse = await fixture.Client.DeleteAsync($"/api/v1/clonehero/songs/{songId}");
        Assert.Equal(System.Net.HttpStatusCode.OK, deleteResponse.StatusCode);

        HttpResponseMessage missingAfterDelete = await fixture.Client.GetAsync($"/api/v1/clonehero/songs/{songId}");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, missingAfterDelete.StatusCode);

        HttpResponseMessage restoreResponse = await fixture.Client.PostAsync($"/api/v1/clonehero/songs/{songId}/restore", content: null);
        Assert.Equal(System.Net.HttpStatusCode.OK, restoreResponse.StatusCode);

        HttpResponseMessage foundAfterRestore = await fixture.Client.GetAsync($"/api/v1/clonehero/songs/{songId}");
        Assert.Equal(System.Net.HttpStatusCode.OK, foundAfterRestore.StatusCode);
    }

    [Fact]
    public async Task InstallFromStagedUnknownJobReturnsNotFound()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/clonehero/install-from-staged/{Guid.NewGuid():D}", content: null);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InstallFromStagedJobWithoutStagedPathReturnsConflict()
    {
        var jobId = Guid.Parse("8c39d7c8-0f84-4ca7-9db0-cd31fcf4a348");
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(store =>
        {
            store.Seed(new DownloadJobResponse
            {
                JobId = jobId,
                Source = "rhythmverse",
                SourceId = "song-1",
                DisplayName = "Artist - Song",
                SourceUrl = "https://example.test/song.zip",
                Stage = "Staged",
                ProgressPercent = 90,
                DownloadedPath = null,
                StagedPath = null,
                InstalledPath = null,
                Error = null,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
        });

        HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/clonehero/install-from-staged/{jobId:D}", content: null);

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task InstallFromStagedBadArtifactPathReturnsBadRequest()
    {
        var jobId = Guid.Parse("37c4bf72-25f5-4975-851d-7f1df86685ac");
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(store =>
        {
            store.Seed(new DownloadJobResponse
            {
                JobId = jobId,
                Source = "rhythmverse",
                SourceId = "song-2",
                DisplayName = "Artist - Song",
                SourceUrl = "https://example.test/song.zip",
                Stage = "Staged",
                ProgressPercent = 90,
                DownloadedPath = null,
                StagedPath = Path.Combine(Path.GetTempPath(), "does-not-exist", "song.zip"),
                InstalledPath = null,
                Error = null,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
        });

        HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/clonehero/install-from-staged/{jobId:D}", content: null);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InstallFromStagedValidZipReturnsAcceptedAndMarksInstalled()
    {
        string stageRoot = Path.Combine(Path.GetTempPath(), "charthub-server-stage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stageRoot);
        string payloadRoot = Path.Combine(stageRoot, "payload");
        Directory.CreateDirectory(payloadRoot);
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "notes.chart"), "chart-data");

        string stagedZip = Path.Combine(stageRoot, "song.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(payloadRoot, stagedZip);

        var jobId = Guid.Parse("d85fad95-5f8b-4c45-81ae-2936e1e27b4f");

        try
        {
            await using TestAppFixture fixture = await TestAppFixture.CreateAsync(store =>
            {
                store.Seed(new DownloadJobResponse
                {
                    JobId = jobId,
                    Source = "rhythmverse",
                    SourceId = "song-3",
                    DisplayName = "Artist - Song",
                    SourceUrl = "https://example.test/song.zip",
                    Stage = "Staged",
                    ProgressPercent = 90,
                    DownloadedPath = null,
                    StagedPath = stagedZip,
                    InstalledPath = null,
                    Error = null,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                });
            });

            HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/clonehero/install-from-staged/{jobId:D}", content: null);

            Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
            Assert.True(fixture.Store.TryGet(jobId, out DownloadJobResponse? updatedJob));
            Assert.NotNull(updatedJob);
            Assert.False(string.IsNullOrWhiteSpace(updatedJob!.InstalledPath));
            Assert.True(Directory.Exists(updatedJob.InstalledPath!));
            Assert.True(File.Exists(Path.Combine(updatedJob.InstalledPath!, "notes.chart")));
        }
        finally
        {
            if (Directory.Exists(stageRoot))
            {
                Directory.Delete(stageRoot, recursive: true);
            }
        }
    }

    private sealed class TestAppFixture : IAsyncDisposable
    {
        private readonly string _cloneHeroRoot;
        private readonly WebApplication _app;

        private TestAppFixture(string cloneHeroRoot, WebApplication app, HttpClient client, FakeDownloadJobStore store)
        {
            _cloneHeroRoot = cloneHeroRoot;
            _app = app;
            Client = client;
            Store = store;
        }

        public HttpClient Client { get; }

        public FakeDownloadJobStore Store { get; }

        public static async Task<TestAppFixture> CreateAsync(Action<FakeDownloadJobStore>? seed = null, bool authenticatedClient = true)
        {
            string cloneHeroRoot = Path.Combine(Path.GetTempPath(), "charthub-server-clonehero", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cloneHeroRoot);

            FakeDownloadJobStore store = new();
            seed?.Invoke(store);

            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();

            if (authenticatedClient)
            {
                builder.Services
                    .AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            }
            else
            {
                builder.Services
                    .AddAuthentication("Reject")
                    .AddScheme<AuthenticationSchemeOptions, RejectAuthHandler>("Reject", _ => { });
            }

            builder.Services.AddAuthorization();
            builder.Services.AddSingleton<IDownloadJobStore>(store);
            builder.Services.AddSingleton<ICloneHeroLibraryService>(_ =>
                new CloneHeroLibraryService(Microsoft.Extensions.Options.Options.Create(new ServerPathOptions
                {
                    CloneHeroRoot = cloneHeroRoot,
                })));

            WebApplication app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapCloneHeroEndpoints();

            await app.StartAsync();
            HttpClient client = app.GetTestClient();

            return new TestAppFixture(cloneHeroRoot, app, client, store);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();

            if (Directory.Exists(_cloneHeroRoot))
            {
                Directory.Delete(_cloneHeroRoot, recursive: true);
            }
        }
    }

    private sealed class RejectAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.Fail("Authentication required."));
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            ClaimsIdentity identity = new(
                [
                    new Claim(ClaimTypes.NameIdentifier, "integration-test-user"),
                    new Claim(ClaimTypes.Email, "integration@test.local"),
                ],
                authenticationType: "Test");
            ClaimsPrincipal principal = new(identity);
            AuthenticationTicket ticket = new(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class FakeDownloadJobStore : IDownloadJobStore
    {
        private readonly Dictionary<Guid, DownloadJobResponse> _jobs = [];

        public void Seed(DownloadJobResponse job) => _jobs[job.JobId] = job;

        public DownloadJobResponse Create(CreateDownloadJobRequest request)
        {
            var response = new DownloadJobResponse
            {
                JobId = Guid.NewGuid(),
                Source = request.Source,
                SourceId = request.SourceId,
                DisplayName = request.DisplayName,
                SourceUrl = request.SourceUrl,
                Stage = "Queued",
                ProgressPercent = 0,
                DownloadedPath = null,
                StagedPath = null,
                InstalledPath = null,
                Error = null,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };

            _jobs[response.JobId] = response;
            return response;
        }

        public IReadOnlyList<DownloadJobResponse> List() => _jobs.Values.ToList();

        public bool TryGet(Guid jobId, out DownloadJobResponse? response)
        {
            if (_jobs.TryGetValue(jobId, out DownloadJobResponse? found))
            {
                response = found;
                return true;
            }

            response = null;
            return false;
        }

        public void QueueRetry(Guid jobId)
        {
        }

        public void RequestCancel(Guid jobId)
        {
        }

        public bool IsCancelRequested(Guid jobId) => false;

        public DownloadJobResponse? TryClaimNextQueuedJob() => null;

        public void UpdateProgress(Guid jobId, string stage, double progressPercent)
        {
        }

        public void MarkDownloaded(Guid jobId, string downloadedPath)
        {
        }

        public void MarkStaged(Guid jobId, string stagedPath)
        {
        }

        public void MarkInstalled(Guid jobId, string installedPath)
        {
            if (_jobs.TryGetValue(jobId, out DownloadJobResponse? existing))
            {
                _jobs[jobId] = new DownloadJobResponse
                {
                    JobId = existing.JobId,
                    Source = existing.Source,
                    SourceId = existing.SourceId,
                    DisplayName = existing.DisplayName,
                    SourceUrl = existing.SourceUrl,
                    Stage = "Completed",
                    ProgressPercent = 100,
                    DownloadedPath = existing.DownloadedPath,
                    StagedPath = existing.StagedPath,
                    InstalledPath = installedPath,
                    Error = existing.Error,
                    CreatedAtUtc = existing.CreatedAtUtc,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
            }
        }

        public void MarkCancelled(Guid jobId)
        {
        }

        public void MarkFailed(Guid jobId, string errorMessage)
        {
        }

        public int DeleteFinishedOlderThan(DateTimeOffset thresholdUtc) => 0;
    }
}
