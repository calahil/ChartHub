using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;

using ChartHub.Server.Contracts;
using ChartHub.Server.Endpoints;
using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
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
        fixture.LibraryService.UpsertInstalledSong(new CloneHeroLibraryUpsertRequest(
            Source: "rhythmverse",
            SourceId: "song-restore-1",
            Artist: "Artist",
            Title: "Title",
            Charter: "Charter",
            SourceMd5: null,
            SourceChartHash: null,
            SourceUrl: "https://example.test/song.zip",
            InstalledPath: "/clonehero/Artist/Title/Charter__rhythmverse",
            InstalledRelativePath: "Artist/Title/Charter__rhythmverse"));

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
    public async Task InstallDownloadJobUnknownJobReturnsNotFound()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/downloads/jobs/{Guid.NewGuid():D}/install", content: null);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InstallDownloadJobWithoutArtifactPathReturnsAcceptedAndEventuallyFailed()
    {
        var jobId = Guid.Parse("8c39d7c8-0f84-4ca7-9db0-cd31fcf4a348");
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(
            seed: store =>
            {
                store.Seed(new DownloadJobResponse
                {
                    JobId = jobId,
                    Source = "rhythmverse",
                    SourceId = "song-1",
                    DisplayName = "Artist - Song",
                    SourceUrl = "https://example.test/song.zip",
                    Stage = "Downloaded",
                    ProgressPercent = 100,
                    DownloadedPath = null,
                    StagedPath = null,
                    InstalledPath = null,
                    Error = null,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                });
            },
            configureInstall: install => install.ExceptionToThrow = new InvalidOperationException("missing artifact"));

        HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/downloads/jobs/{jobId:D}/install", content: null);

        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        DownloadJobResponse updatedJob = await WaitForJobStageAsync(fixture.Store, jobId, "Failed");
        Assert.Equal("Failed", updatedJob.Stage);
        Assert.Equal("missing artifact", updatedJob.Error);
    }

    [Fact]
    public async Task InstallDownloadJobWithRelativeArtifactPathResolvesAgainstContentRoot()
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), "charthub-server-install-relative", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);

        try
        {
            var jobId = Guid.Parse("09f7484b-f5f8-4fce-9bd0-74ccb62f4e0d");
            string relativePath = Path.Combine("dev-data", "charthub", "downloads", $"school-{jobId:D}.bin");
            string absolutePath = Path.Combine(contentRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            await File.WriteAllBytesAsync(absolutePath, [0x01, 0x02, 0x03]);

            await using TestAppFixture fixture = await TestAppFixture.CreateAsync(
                seed: store =>
                {
                    store.Seed(new DownloadJobResponse
                    {
                        JobId = jobId,
                        Source = "rhythmverse",
                        SourceId = "song-relative",
                        DisplayName = "Artist - Song",
                        SourceUrl = "https://example.test/song.bin",
                        Stage = "Downloaded",
                        ProgressPercent = 100,
                        DownloadedPath = relativePath,
                        StagedPath = null,
                        InstalledPath = null,
                        Error = null,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                    });
                },
                configureInstall: install => install.ResultPath = Path.Combine(contentRoot, "installed"),
                contentRootPath: contentRoot);

            HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/downloads/jobs/{jobId:D}/install", content: null);

            Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
            DownloadJobResponse updatedJob = await WaitForJobStageAsync(fixture.Store, jobId, "Installed");
            Assert.False(string.IsNullOrWhiteSpace(updatedJob!.StagedPath));
            Assert.True(Path.IsPathRooted(updatedJob.StagedPath));
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InstallDownloadJobNonInstallableStageReturnsConflict()
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
                Stage = "Installed",
                ProgressPercent = 100,
                DownloadedPath = "/tmp/song.zip",
                StagedPath = "/tmp/song.zip",
                InstalledPath = null,
                Error = null,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
        });

        HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/downloads/jobs/{jobId:D}/install", content: null);

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task InstallDownloadJobValidDownloadedArtifactReturnsAcceptedAndMarksInstalled()
    {
        var jobId = Guid.Parse("d85fad95-5f8b-4c45-81ae-2936e1e27b4f");
        string installedPath = Path.Combine(Path.GetTempPath(), "charthub-server-installed", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installedPath);

        try
        {
            await using TestAppFixture fixture = await TestAppFixture.CreateAsync(
                seed: store =>
                {
                    store.Seed(new DownloadJobResponse
                    {
                        JobId = jobId,
                        Source = "rhythmverse",
                        SourceId = "song-3",
                        DisplayName = "Artist - Song",
                        SourceUrl = "https://example.test/song.zip",
                        Stage = "Downloaded",
                        ProgressPercent = 100,
                        DownloadedPath = "/tmp/song.zip",
                        StagedPath = "/tmp/song.zip",
                        InstalledPath = null,
                        Error = null,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                    });
                },
                configureInstall: install => install.ResultPath = installedPath);

            HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/downloads/jobs/{jobId:D}/install", content: null);

            Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
            DownloadJobResponse updatedJob = await WaitForJobStageAsync(fixture.Store, jobId, "Installed");
            Assert.False(string.IsNullOrWhiteSpace(updatedJob!.InstalledPath));
            Assert.Equal(installedPath, updatedJob.InstalledPath);
            Assert.Equal("Installed", updatedJob.Stage);
        }
        finally
        {
            if (Directory.Exists(installedPath))
            {
                Directory.Delete(installedPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InstallDownloadJobWhenPipelineThrowsReturnsAcceptedAndEventuallyFailed()
    {
        var jobId = Guid.Parse("3f3c5d66-63fa-4b95-97c3-a1f4673432f8");

        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(
            seed: store =>
            {
                store.Seed(new DownloadJobResponse
                {
                    JobId = jobId,
                    Source = "rhythmverse",
                    SourceId = "song-4",
                    DisplayName = "Artist - Song",
                    SourceUrl = "https://example.test/song.zip",
                    Stage = "Downloaded",
                    ProgressPercent = 100,
                    DownloadedPath = "/tmp/song.zip",
                    StagedPath = "/tmp/song.zip",
                    InstalledPath = null,
                    Error = null,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                });
            },
            configureInstall: install => install.ExceptionToThrow = new InvalidOperationException("install failed"));

        HttpResponseMessage response = await fixture.Client.PostAsync($"/api/v1/downloads/jobs/{jobId:D}/install", content: null);

        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        DownloadJobResponse updatedJob = await WaitForJobStageAsync(fixture.Store, jobId, "Failed");
        Assert.Equal("Failed", updatedJob.Stage);
        Assert.Equal("install failed", updatedJob.Error);
    }

    private static async Task<DownloadJobResponse> WaitForJobStageAsync(
        FakeDownloadJobStore store,
        Guid jobId,
        string expectedStage,
        int maxAttempts = 50,
        int delayMs = 20)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (store.TryGet(jobId, out DownloadJobResponse? job)
                && job is not null
                && string.Equals(job.Stage, expectedStage, StringComparison.OrdinalIgnoreCase))
            {
                return job;
            }

            await Task.Delay(delayMs);
        }

        Assert.True(store.TryGet(jobId, out DownloadJobResponse? finalJob));
        Assert.NotNull(finalJob);
        Assert.Equal(expectedStage, finalJob!.Stage);
        return finalJob;
    }

    private sealed class TestAppFixture : IAsyncDisposable
    {
        private readonly string _cloneHeroRoot;
        private readonly WebApplication _app;

        private TestAppFixture(
            string cloneHeroRoot,
            WebApplication app,
            HttpClient client,
            FakeDownloadJobStore store,
            ICloneHeroLibraryService libraryService)
        {
            _cloneHeroRoot = cloneHeroRoot;
            _app = app;
            Client = client;
            Store = store;
            LibraryService = libraryService;
        }

        public HttpClient Client { get; }

        public FakeDownloadJobStore Store { get; }

        public ICloneHeroLibraryService LibraryService { get; }

        public static async Task<TestAppFixture> CreateAsync(Action<FakeDownloadJobStore>? seed = null, bool authenticatedClient = true)
            => await CreateAsync(seed, configureInstall: null, authenticatedClient: authenticatedClient);

        public static async Task<TestAppFixture> CreateAsync(
            Action<FakeDownloadJobStore>? seed,
            Action<FakeDownloadJobInstallService>? configureInstall,
            bool authenticatedClient = true,
            string? contentRootPath = null)
        {
            string rootBase = contentRootPath ?? Path.Combine(Path.GetTempPath(), "charthub-server-content-root", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootBase);
            string cloneHeroRoot = Path.Combine(rootBase, "clonehero");
            Directory.CreateDirectory(cloneHeroRoot);

            FakeDownloadJobStore store = new();
            seed?.Invoke(store);
            FakeDownloadJobInstallService installService = new();
            configureInstall?.Invoke(installService);

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
            builder.Services.AddSingleton<IDownloadJobInstallService>(installService);
            builder.Services.AddSingleton<IInstallConcurrencyLimiter, SemaphoreInstallConcurrencyLimiter>();
            builder.Services.AddSingleton<IJobLogSink, NullJobLogSink>();
            builder.Services.AddSingleton<ICloneHeroLibraryService>(_ =>
                new CloneHeroLibraryService(Microsoft.Extensions.Options.Options.Create(new ServerPathOptions
                {
                    CloneHeroRoot = cloneHeroRoot,
                    SqliteDbPath = Path.Combine(rootBase, "charthub-server.db"),
                }), new TestHostEnvironment(rootBase), new ServerCloneHeroDirectorySchemaService()));
            builder.Services.AddSingleton<ISongIniPatchService, SongIniPatchService>();
            builder.Services.AddSingleton<ITranscriptionJobStore>(new NullTranscriptionJobStore());

            WebApplication app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapCloneHeroEndpoints();
            app.MapDownloadEndpoints();

            await app.StartAsync();
            HttpClient client = app.GetTestClient();
            ICloneHeroLibraryService libraryService = app.Services.GetRequiredService<ICloneHeroLibraryService>();

            return new TestAppFixture(cloneHeroRoot, app, client, store, libraryService);
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
            if (_jobs.TryGetValue(jobId, out DownloadJobResponse? existing))
            {
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
                    Error = existing.Error,
                    CreatedAtUtc = existing.CreatedAtUtc,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
            }
        }

        public void SetDownloadedArtifact(Guid jobId, string downloadedPath, string fileType)
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
                    Stage = "Downloaded",
                    ProgressPercent = 100,
                    DownloadedPath = downloadedPath,
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
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
            }
        }

        public void UpdateFileType(Guid jobId, string fileType)
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
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
            }
        }

        public IReadOnlyList<DownloadJobResponse> ListDownloadedWithoutFileType()
            => [];

        public void MarkStaged(Guid jobId, string stagedPath)
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
                    Stage = "Staged",
                    ProgressPercent = 92,
                    DownloadedPath = existing.DownloadedPath,
                    StagedPath = stagedPath,
                    InstalledPath = existing.InstalledPath,
                    Error = existing.Error,
                    CreatedAtUtc = existing.CreatedAtUtc,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
            }
        }

        public void MarkInstalled(
            Guid jobId,
            string installedPath,
            string? installedRelativePath = null,
            string? artist = null,
            string? title = null,
            string? charter = null,
            string? sourceMd5 = null,
            string? sourceChartHash = null,
            IReadOnlyList<DownloadJobStatus>? conversionStatuses = null)
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
                    ConversionStatuses = conversionStatuses ?? existing.ConversionStatuses,
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
            if (_jobs.TryGetValue(jobId, out DownloadJobResponse? existing))
            {
                _jobs[jobId] = new DownloadJobResponse
                {
                    JobId = existing.JobId,
                    Source = existing.Source,
                    SourceId = existing.SourceId,
                    DisplayName = existing.DisplayName,
                    SourceUrl = existing.SourceUrl,
                    Stage = "Failed",
                    ProgressPercent = 100,
                    DownloadedPath = existing.DownloadedPath,
                    StagedPath = existing.StagedPath,
                    InstalledPath = existing.InstalledPath,
                    Error = errorMessage,
                    CreatedAtUtc = existing.CreatedAtUtc,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
            }
        }

        public int DeleteFinishedOlderThan(DateTimeOffset thresholdUtc) => 0;

        public void DeleteJob(Guid jobId) => _jobs.Remove(jobId);
    }

    private sealed class FakeDownloadJobInstallService : IDownloadJobInstallService
    {
        public string ResultPath { get; set; } = "/tmp/installed-song";

        public string StagedPath { get; set; } = "/tmp/staged-song.zip";

        public Exception? ExceptionToThrow { get; set; }

        public Task<DownloadJobInstallResult> InstallJobAsync(DownloadJobResponse job, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(new DownloadJobInstallResult(
                StagedPath,
                ResultPath,
                "Artist/Title/Charter__rhythmverse",
                new ServerSongMetadata("Artist", "Title", "Charter")));
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ChartHub.Server.Tests";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = contentRootPath;

        public string EnvironmentName { get; set; } = Environments.Development;

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }

    private sealed class NullJobLogSink : IJobLogSink
    {
        public void Add(Guid jobId, LogLevel level, EventId eventId, string? category, string message, string? exception) { }

        public IReadOnlyList<JobLogEntry> GetLogs(Guid jobId) => [];
    }

    private sealed class NullTranscriptionJobStore : ITranscriptionJobStore
    {
        public TranscriptionJob CreateJob(string songId, string songFolderPath, TranscriptionAggressiveness aggressiveness, int attemptNumber = 1)
            => throw new NotSupportedException();

        public TranscriptionJob? TryClaimNext(string runnerId) => null;

        public void UpdateStatus(string jobId, TranscriptionJobStatus status, string? failureReason = null) { }

        public void MarkCompleted(string jobId, string midiFilePath) { }

        public IReadOnlyList<TranscriptionJob> ListJobs(string? songId = null, TranscriptionJobStatus? status = null) => [];

        public TranscriptionJob? GetJob(string jobId) => null;

        public bool DeleteJob(string jobId) => false;

        public TranscriptionResult? GetLatestApprovedResult(string songId) => null;

        public IReadOnlyList<TranscriptionResult> ListResults(string? songId = null) => [];

        public void ApproveResult(string resultId) { }
    }
}
