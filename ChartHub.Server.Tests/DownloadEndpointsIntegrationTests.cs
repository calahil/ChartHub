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

public sealed class DownloadEndpointsIntegrationTests
{
    [Fact]
    public async Task CreateJobAuthenticatedReturns201WithJobBody()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/downloads/jobs",
            new CreateDownloadJobRequest
            {
                Source = "rhythmverse",
                SourceId = "rv-1",
                DisplayName = "Test Song",
                SourceUrl = "https://rhythmverse.co/download/rv-1",
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        DownloadJobResponse? job = await response.Content.ReadFromJsonAsync<DownloadJobResponse>();
        Assert.NotNull(job);
        Assert.Equal("Test Song", job!.DisplayName);
        Assert.Equal("rhythmverse", job.Source);
        Assert.Equal("Queued", job.Stage);
    }

    [Fact]
    public async Task CreateJobUnauthenticatedReturns401()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(authenticatedClient: false);

        HttpResponseMessage response = await fixture.Client.PostAsJsonAsync(
            "/api/v1/downloads/jobs",
            new CreateDownloadJobRequest
            {
                Source = "rhythmverse",
                SourceId = "rv-unauth",
                DisplayName = "Unauth Song",
                SourceUrl = "https://rhythmverse.co/download/rv-unauth",
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListJobsReturnsAllSeededJobs()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(seed: store =>
        {
            store.Seed(MakeJob(id1, "Song One"));
            store.Seed(MakeJob(id2, "Song Two"));
        });

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/downloads/jobs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<DownloadJobResponse>? jobs = await response.Content.ReadFromJsonAsync<IReadOnlyList<DownloadJobResponse>>();
        Assert.NotNull(jobs);
        Assert.Equal(2, jobs!.Count);
    }

    [Fact]
    public async Task ListJobsUnauthenticatedReturns401()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(authenticatedClient: false);

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/downloads/jobs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetJobKnownIdReturns200WithBody()
    {
        var id = Guid.NewGuid();

        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(seed: store =>
            store.Seed(MakeJob(id, "Known Song")));

        HttpResponseMessage response = await fixture.Client.GetAsync($"/api/v1/downloads/jobs/{id:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        DownloadJobResponse? job = await response.Content.ReadFromJsonAsync<DownloadJobResponse>();
        Assert.NotNull(job);
        Assert.Equal(id, job!.JobId);
        Assert.Equal("Known Song", job.DisplayName);
    }

    [Fact]
    public async Task GetJobUnknownIdReturns404()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.GetAsync($"/api/v1/downloads/jobs/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RetryJobKnownIdReturns202()
    {
        var id = Guid.NewGuid();

        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(seed: store =>
            store.Seed(MakeJob(id, "Retry Song", stage: "Failed")));

        HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/downloads/jobs/{id:D}/retry", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task RetryJobUnknownIdReturns404()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/downloads/jobs/{Guid.NewGuid():D}/retry", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CancelJobKnownIdReturns202()
    {
        var id = Guid.NewGuid();

        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(seed: store =>
            store.Seed(MakeJob(id, "Cancel Song", stage: "Downloading")));

        HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/downloads/jobs/{id:D}/cancel", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task CancelJobUnknownIdReturns404()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/downloads/jobs/{Guid.NewGuid():D}/cancel", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteJobKnownIdReturns204AndJobIsGone()
    {
        var id = Guid.NewGuid();

        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(seed: store =>
            store.Seed(MakeJob(id, "Delete Song")));

        HttpResponseMessage deleteResponse = await fixture.Client.DeleteAsync($"/api/v1/downloads/jobs/{id:D}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage getResponse = await fixture.Client.GetAsync($"/api/v1/downloads/jobs/{id:D}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteJobUnknownIdReturns404()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.DeleteAsync($"/api/v1/downloads/jobs/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetJobLogsKnownIdReturns200WithLogEntries()
    {
        var id = Guid.NewGuid();
        var sink = new StubJobLogSink();
        sink.Add(id, LogLevel.Information, new EventId(1, "TestEvent"), null, "Install started", null);

        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(
            seed: store => store.Seed(MakeJob(id, "Logged Song")),
            logSink: sink);

        HttpResponseMessage response = await fixture.Client.GetAsync($"/api/v1/downloads/jobs/{id:D}/logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<JobLogEntryResponse>? logs = await response.Content.ReadFromJsonAsync<IReadOnlyList<JobLogEntryResponse>>();
        Assert.NotNull(logs);
        JobLogEntryResponse entry = Assert.Single(logs!);
        Assert.Equal("Install started", entry.Message);
    }

    [Fact]
    public async Task GetJobLogsUnknownIdReturns404()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.GetAsync($"/api/v1/downloads/jobs/{Guid.NewGuid():D}/logs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetJobLogsUnauthenticatedReturns401()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(authenticatedClient: false);

        HttpResponseMessage response = await fixture.Client.GetAsync($"/api/v1/downloads/jobs/{Guid.NewGuid():D}/logs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StreamJobsReturns200WithEventStreamContentType()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/downloads/jobs/stream");

        HttpResponseMessage response = await fixture.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertEventStreamContentType(response);
    }

    [Fact]
    public async Task StreamJobsUnauthenticatedReturns401()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(authenticatedClient: false);

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/downloads/jobs/stream");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StreamJobKnownIdReturns200WithEventStreamContentType()
    {
        var id = Guid.NewGuid();

        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(seed: store =>
            store.Seed(MakeJob(id, "Streaming Song", stage: "Downloading")));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/downloads/jobs/{id:D}/stream");

        HttpResponseMessage response = await fixture.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertEventStreamContentType(response);
    }

    [Fact]
    public async Task StreamJobUnknownIdReturns404()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.GetAsync($"/api/v1/downloads/jobs/{Guid.NewGuid():D}/stream");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("InstallQueued")]
    [InlineData("Staging")]
    [InlineData("Installing")]
    public async Task InstallJobAlreadyInProgressReturns202WithCurrentJob(string inProgressStage)
    {
        var id = Guid.NewGuid();

        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(seed: store =>
            store.Seed(MakeJob(id, "In-Progress Song", stage: inProgressStage)));

        HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/downloads/jobs/{id:D}/install", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        DownloadJobResponse? job = await response.Content.ReadFromJsonAsync<DownloadJobResponse>();
        Assert.NotNull(job);
        Assert.Equal(inProgressStage, job!.Stage);
    }

    private static DownloadJobResponse MakeJob(Guid id, string displayName, string stage = "Queued") =>
        new()
        {
            JobId = id,
            Source = "rhythmverse",
            SourceId = $"rv-{id:N}",
            DisplayName = displayName,
            SourceUrl = $"https://rhythmverse.co/download/{id:N}",
            Stage = stage,
            ProgressPercent = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

    private static void AssertEventStreamContentType(HttpResponseMessage response)
    {
        string? contentType = response.Content.Headers.ContentType?.MediaType;

        if (string.IsNullOrEmpty(contentType) && response.Headers.TryGetValues("Content-Type", out IEnumerable<string>? headerVals))
        {
            contentType = string.Join(";", headerVals);
        }

        Assert.True(
            contentType?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true,
            $"Expected text/event-stream but got: {contentType}");
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
            Action<FakeDownloadJobStore>? seed = null,
            IJobLogSink? logSink = null,
            bool authenticatedClient = true)
        {
            var store = new FakeDownloadJobStore();
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
            builder.Services.AddSingleton<IDownloadJobInstallService>(new FakeDownloadJobInstallService());
            builder.Services.AddSingleton<IInstallConcurrencyLimiter, SemaphoreInstallConcurrencyLimiter>();
            builder.Services.AddSingleton<IJobLogSink>(logSink ?? new NullJobLogSink());
            builder.Services.AddSingleton<ICloneHeroLibraryService>(new NullCloneHeroLibraryService());

            WebApplication app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapDownloadEndpoints();

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

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "download-test-user"),
                    new Claim(ClaimTypes.Email, "download-test@test.local"),
                ],
                authenticationType: "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
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
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };

            _jobs[response.JobId] = response;
            return response;
        }

        public IReadOnlyList<DownloadJobResponse> List() => [.. _jobs.Values];

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

        public void QueueRetry(Guid jobId) { }

        public void RequestCancel(Guid jobId) { }

        public bool IsCancelRequested(Guid jobId) => false;

        public DownloadJobResponse? TryClaimNextQueuedJob() => null;

        public void UpdateProgress(Guid jobId, string stage, double progressPercent)
        {
            if (!_jobs.TryGetValue(jobId, out DownloadJobResponse? existing))
            {
                return;
            }

            _jobs[jobId] = new DownloadJobResponse
            {
                JobId = existing.JobId,
                Source = existing.Source,
                SourceId = existing.SourceId,
                DisplayName = existing.DisplayName,
                SourceUrl = existing.SourceUrl,
                Stage = stage,
                ProgressPercent = progressPercent,
                DownloadedPath = existing.DownloadedPath,
                StagedPath = existing.StagedPath,
                InstalledPath = existing.InstalledPath,
                InstalledRelativePath = existing.InstalledRelativePath,
                Artist = existing.Artist,
                Title = existing.Title,
                Charter = existing.Charter,
                SourceMd5 = existing.SourceMd5,
                SourceChartHash = existing.SourceChartHash,
                Error = existing.Error,
                FileType = existing.FileType,
                CreatedAtUtc = existing.CreatedAtUtc,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        public void MarkDownloaded(Guid jobId, string downloadedPath)
        {
            if (!_jobs.TryGetValue(jobId, out DownloadJobResponse? existing))
            {
                return;
            }

            _jobs[jobId] = new DownloadJobResponse
            {
                JobId = existing.JobId,
                Source = existing.Source,
                SourceId = existing.SourceId,
                DisplayName = existing.DisplayName,
                SourceUrl = existing.SourceUrl,
                Stage = "Downloaded",
                ProgressPercent = 100,
                DownloadedPath = downloadedPath,
                StagedPath = existing.StagedPath,
                InstalledPath = existing.InstalledPath,
                CreatedAtUtc = existing.CreatedAtUtc,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        public void UpdateFileType(Guid jobId, string fileType)
        {
            if (!_jobs.TryGetValue(jobId, out DownloadJobResponse? existing))
            {
                return;
            }

            _jobs[jobId] = new DownloadJobResponse
            {
                JobId = existing.JobId,
                Source = existing.Source,
                SourceId = existing.SourceId,
                DisplayName = existing.DisplayName,
                SourceUrl = existing.SourceUrl,
                Stage = existing.Stage,
                ProgressPercent = existing.ProgressPercent,
                DownloadedPath = existing.DownloadedPath,
                StagedPath = existing.StagedPath,
                InstalledPath = existing.InstalledPath,
                InstalledRelativePath = existing.InstalledRelativePath,
                Artist = existing.Artist,
                Title = existing.Title,
                Charter = existing.Charter,
                SourceMd5 = existing.SourceMd5,
                SourceChartHash = existing.SourceChartHash,
                Error = existing.Error,
                FileType = fileType,
                CreatedAtUtc = existing.CreatedAtUtc,
                UpdatedAtUtc = existing.UpdatedAtUtc,
            };
        }

        public IReadOnlyList<DownloadJobResponse> ListDownloadedWithoutFileType() => [];

        public void MarkStaged(Guid jobId, string stagedPath)
        {
            if (!_jobs.TryGetValue(jobId, out DownloadJobResponse? existing))
            {
                return;
            }

            _jobs[jobId] = new DownloadJobResponse
            {
                JobId = existing.JobId,
                Source = existing.Source,
                SourceId = existing.SourceId,
                DisplayName = existing.DisplayName,
                SourceUrl = existing.SourceUrl,
                Stage = "Staged",
                ProgressPercent = 92,
                DownloadedPath = existing.DownloadedPath,
                StagedPath = stagedPath,
                InstalledPath = existing.InstalledPath,
                CreatedAtUtc = existing.CreatedAtUtc,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        public void MarkInstalled(
            Guid jobId,
            string installedPath,
            string? installedRelativePath = null,
            string? artist = null,
            string? title = null,
            string? charter = null,
            string? sourceMd5 = null,
            string? sourceChartHash = null)
        {
            if (!_jobs.TryGetValue(jobId, out DownloadJobResponse? existing))
            {
                return;
            }

            _jobs[jobId] = new DownloadJobResponse
            {
                JobId = existing.JobId,
                Source = existing.Source,
                SourceId = existing.SourceId,
                DisplayName = existing.DisplayName,
                SourceUrl = existing.SourceUrl,
                Stage = "Installed",
                ProgressPercent = 100,
                DownloadedPath = existing.DownloadedPath,
                StagedPath = existing.StagedPath,
                InstalledPath = installedPath,
                InstalledRelativePath = installedRelativePath,
                Artist = artist ?? existing.Artist,
                Title = title ?? existing.Title,
                Charter = charter ?? existing.Charter,
                SourceMd5 = sourceMd5 ?? existing.SourceMd5,
                SourceChartHash = sourceChartHash ?? existing.SourceChartHash,
                CreatedAtUtc = existing.CreatedAtUtc,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        public void MarkCancelled(Guid jobId) { }

        public void MarkFailed(Guid jobId, string errorMessage)
        {
            if (!_jobs.TryGetValue(jobId, out DownloadJobResponse? existing))
            {
                return;
            }

            _jobs[jobId] = new DownloadJobResponse
            {
                JobId = existing.JobId,
                Source = existing.Source,
                SourceId = existing.SourceId,
                DisplayName = existing.DisplayName,
                SourceUrl = existing.SourceUrl,
                Stage = "Failed",
                ProgressPercent = existing.ProgressPercent,
                DownloadedPath = existing.DownloadedPath,
                Error = errorMessage,
                CreatedAtUtc = existing.CreatedAtUtc,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        public int DeleteFinishedOlderThan(DateTimeOffset thresholdUtc) => 0;

        public void DeleteJob(Guid jobId) => _jobs.Remove(jobId);
    }

    private sealed class FakeDownloadJobInstallService : IDownloadJobInstallService
    {
        public Task<DownloadJobInstallResult> InstallJobAsync(DownloadJobResponse job, CancellationToken cancellationToken = default)
            => Task.FromResult(new DownloadJobInstallResult(
                "/tmp/staged",
                "/tmp/installed",
                "Artist/Title/Charter__rhythmverse",
                new ServerSongMetadata("Artist", "Title", "Charter")));
    }

    private sealed class NullCloneHeroLibraryService : ICloneHeroLibraryService
    {
        public IReadOnlyList<CloneHeroSongResponse> ListSongs() => [];

        public bool TryGetSong(string songId, out CloneHeroSongResponse? song)
        {
            song = null;
            return false;
        }

        public bool TrySoftDeleteSong(string songId, out CloneHeroSongResponse? song)
        {
            song = null;
            return false;
        }

        public bool TryRestoreSong(string songId, out CloneHeroSongResponse? song)
        {
            song = null;
            return false;
        }

        public void UpsertInstalledSong(CloneHeroLibraryUpsertRequest request) { }
    }

    private sealed class NullJobLogSink : IJobLogSink
    {
        public void Add(Guid jobId, LogLevel level, EventId eventId, string? category, string message, string? exception) { }

        public IReadOnlyList<JobLogEntry> GetLogs(Guid jobId) => [];
    }

    private sealed class StubJobLogSink : IJobLogSink
    {
        private readonly Dictionary<Guid, List<JobLogEntry>> _entries = [];

        public void Add(Guid jobId, LogLevel level, EventId eventId, string? category, string message, string? exception)
        {
            if (!_entries.TryGetValue(jobId, out List<JobLogEntry>? list))
            {
                list = [];
                _entries[jobId] = list;
            }

            list.Add(new JobLogEntry(
                DateTimeOffset.UtcNow,
                level.ToString(),
                eventId.Id,
                category,
                message,
                exception));
        }

        public IReadOnlyList<JobLogEntry> GetLogs(Guid jobId)
            => _entries.TryGetValue(jobId, out List<JobLogEntry>? list) ? list : [];
    }
}
