using System.Runtime.CompilerServices;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class DownloadViewModelTests
{
    [Fact]
    public async Task CheckAllCommand_UpdatesQueueSelectionFlags()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-checks");
        using AppGlobalSettings settings = CreateSettings(
            temp.RootPath,
            serverApiBaseUrl: "http://127.0.0.1:5001",
            serverApiAuthToken: "token");
        var sharedQueue = new SharedDownloadQueue();
        var sut = new ViewModels.DownloadViewModel(
            settings,
            new FakeChartHubServerApiClient(),
            sharedQueue,
            uiInvoke: action =>
            {
                action();
                return Task.CompletedTask;
            });

        try
        {
            sut.IngestionQueue.Add(CreateQueueItem(1));
            sut.IngestionQueue.Add(CreateQueueItem(2));

            sut.CheckAllCommand.Execute(null);

            Assert.True(sut.IsAllChecked);
            Assert.All(sut.IngestionQueue, item => Assert.True(item.Checked));
            Assert.True(sut.IsAnyChecked);

            sut.IngestionQueue[0].Checked = false;
            sut.IngestionQueue[1].Checked = false;

            Assert.False(sut.IsAnyChecked);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    [Fact]
    public async Task InstallSongsCommand_InstallsSelectedQueueDownloads()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-install");
        using AppGlobalSettings settings = CreateSettings(
            temp.RootPath,
            serverApiBaseUrl: "http://127.0.0.1:5001",
            serverApiAuthToken: "token");
        string selectedFilePath = temp.GetPath("song-a.zip");
        await File.WriteAllTextAsync(selectedFilePath, "zip");

        var sharedQueue = new SharedDownloadQueue();
        var fakeClient = new FakeChartHubServerApiClient
        {
            Jobs =
            [
                new ChartHubServerDownloadJobResponse(
                    new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    LibrarySourceNames.RhythmVerse,
                    "id-1",
                    "song-a.zip",
                    "https://example.test/song-a",
                    "Downloaded",
                    100,
                    selectedFilePath,
                    selectedFilePath,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
            ],
        };

        var sut = new ViewModels.DownloadViewModel(
            settings,
            fakeClient,
            sharedQueue,
            uiInvoke: action =>
            {
                action();
                return Task.CompletedTask;
            });

        try
        {
            bool queueLoaded = await WaitForConditionAsync(() => sut.IngestionQueue.Count == 1, TimeSpan.FromSeconds(2));
            Assert.True(queueLoaded);

            sut.IngestionQueue[0].Checked = true;

            await sut.InstallSongsCommand();

            Assert.Single(fakeClient.InstallRequestedJobIds);
            Assert.True(sut.IsInstallPanelVisible);
            Assert.True(sut.IsInstallActive);
            Assert.DoesNotContain("No selected downloads are installable", sut.InstallStageText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    [Fact]
    public async Task DeleteSelectedDownloadsCommand_WhenServerConnected_CancelsSelectedJobs()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-delete");
        using AppGlobalSettings settings = CreateSettings(
            temp.RootPath,
            serverApiBaseUrl: "http://127.0.0.1:5001",
            serverApiAuthToken: "token");

        var fakeClient = new FakeChartHubServerApiClient
        {
            Jobs =
            [
                new ChartHubServerDownloadJobResponse(
                    new Guid("11111111-1111-1111-1111-111111111111"),
                    LibrarySourceNames.RhythmVerse,
                    "id-1",
                    "Song A",
                    "https://example.test/song-a",
                    "Downloaded",
                    100,
                    "/tmp/song-a.zip",
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
            ],
        };
        var sharedQueue = new SharedDownloadQueue();

        var sut = new ViewModels.DownloadViewModel(
            settings,
            fakeClient,
            sharedQueue,
            uiInvoke: action =>
            {
                action();
                return Task.CompletedTask;
            });

        try
        {
            bool queueLoaded = await WaitForConditionAsync(() => sut.IngestionQueue.Count == 1, TimeSpan.FromSeconds(2));
            Assert.True(queueLoaded);

            sut.IngestionQueue[0].Checked = true;
            await sut.DeleteSelectedDownloadsCommand.ExecuteAsync(null);

            Assert.Single(fakeClient.CancelledJobIds);
            Assert.Equal(new Guid("11111111-1111-1111-1111-111111111111"), fakeClient.CancelledJobIds[0]);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    [Fact]
    public async Task RefreshQueue_HidesInstalledAndCompletedJobs()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-hide-installed");
        using AppGlobalSettings settings = CreateSettings(
            temp.RootPath,
            serverApiBaseUrl: "http://127.0.0.1:5001",
            serverApiAuthToken: "token");

        var fakeClient = new FakeChartHubServerApiClient
        {
            Jobs =
            [
                new ChartHubServerDownloadJobResponse(
                    new Guid("33333333-3333-3333-3333-333333333333"),
                    LibrarySourceNames.RhythmVerse,
                    "id-a",
                    "Installed Song",
                    "https://example.test/song-a",
                    "Installed",
                    100,
                    "/tmp/song-a.zip",
                    null,
                    "/tmp/installed/song-a",
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
                new ChartHubServerDownloadJobResponse(
                    new Guid("44444444-4444-4444-4444-444444444444"),
                    LibrarySourceNames.RhythmVerse,
                    "id-b",
                    "Completed Song",
                    "https://example.test/song-b",
                    "Completed",
                    100,
                    "/tmp/song-b.zip",
                    null,
                    "/tmp/installed/song-b",
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
                new ChartHubServerDownloadJobResponse(
                    new Guid("55555555-5555-5555-5555-555555555555"),
                    LibrarySourceNames.RhythmVerse,
                    "id-c",
                    "Downloaded Song",
                    "https://example.test/song-c",
                    "Downloaded",
                    100,
                    "/tmp/song-c.zip",
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
            ],
        };

        var sut = new ViewModels.DownloadViewModel(
            settings,
            fakeClient,
            new SharedDownloadQueue(),
            uiInvoke: action =>
            {
                action();
                return Task.CompletedTask;
            });

        try
        {
            bool queueLoaded = await WaitForConditionAsync(() => sut.IngestionQueue.Count == 1, TimeSpan.FromSeconds(2));
            Assert.True(queueLoaded);
            Assert.Single(sut.IngestionQueue);
            Assert.Equal("Downloaded Song", sut.IngestionQueue[0].DisplayName);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    [Fact]
    public async Task StreamJobUpdates_RefreshesSharedDownloadCardState()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-stream-shared-card");
        using AppGlobalSettings settings = CreateSettings(
            temp.RootPath,
            serverApiBaseUrl: "http://127.0.0.1:5001",
            serverApiAuthToken: "token");

        var jobId = new Guid("22222222-2222-2222-2222-222222222222");
        var sharedQueue = new SharedDownloadQueue();
        sharedQueue.Downloads.Add(new DownloadFile("Song B", temp.RootPath, jobId.ToString("D"), null)
        {
            Status = "Queued",
            DownloadProgress = 0,
        });

        var fakeClient = new FakeChartHubServerApiClient
        {
            Jobs =
            [
                new ChartHubServerDownloadJobResponse(
                    jobId,
                    LibrarySourceNames.RhythmVerse,
                    "id-2",
                    "Song B",
                    "https://example.test/song-b",
                    "Queued",
                    0,
                    null,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
            ],
            StreamBatches =
            [
                [
                    new ChartHubServerDownloadProgressEvent(
                        jobId,
                        "Installed",
                        100,
                        DateTimeOffset.UtcNow),
                ],
            ],
        };
        fakeClient.OnStreamBatch = _ =>
        {
            fakeClient.Jobs =
            [
                new ChartHubServerDownloadJobResponse(
                    jobId,
                    LibrarySourceNames.RhythmVerse,
                    "id-2",
                    "Song B",
                    "https://example.test/song-b",
                    "Installed",
                    100,
                    "/tmp/song-b.zip",
                    null,
                    "/tmp/installed/song-b",
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
            ];
        };

        var sut = new ViewModels.DownloadViewModel(
            settings,
            fakeClient,
            sharedQueue,
            uiInvoke: action =>
            {
                action();
                return Task.CompletedTask;
            });

        try
        {
            bool cardUpdated = await WaitForConditionAsync(
                () => sharedQueue.Downloads[0].Finished && string.Equals(sharedQueue.Downloads[0].Status, "Installed", StringComparison.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(2));

            Assert.True(cardUpdated);
            Assert.Equal(100, sharedQueue.Downloads[0].DownloadProgress);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    private static IngestionQueueItem CreateQueueItem(long id)
    {
        return new IngestionQueueItem
        {
            IngestionId = id,
            DisplayName = $"song-{id}.zip",
            CurrentState = IngestionState.Downloaded,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static AppGlobalSettings CreateSettings(
        string rootPath,
        string serverApiBaseUrl = "",
        string serverApiAuthToken = "")
    {
        var orchestrator = new FakeSettingsOrchestrator(new AppConfigRoot
        {
            Runtime = new RuntimeAppConfig
            {
                ServerApiBaseUrl = serverApiBaseUrl,
                ServerApiAuthToken = serverApiAuthToken,
            },
        });

        return new AppGlobalSettings(orchestrator);
    }

    private static async Task<bool> WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return predicate();
    }

    private sealed class FakeSettingsOrchestrator : ISettingsOrchestrator
    {
        public FakeSettingsOrchestrator(AppConfigRoot current)
        {
            Current = current;
        }

        public AppConfigRoot Current { get; private set; }
        public event Action<AppConfigRoot>? SettingsChanged;

        public Task<ConfigValidationResult> UpdateAsync(Action<AppConfigRoot> update, CancellationToken cancellationToken = default)
        {
            update(Current);
            SettingsChanged?.Invoke(Current);
            return Task.FromResult(ConfigValidationResult.Success);
        }

        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            SettingsChanged?.Invoke(Current);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeChartHubServerApiClient : IChartHubServerApiClient
    {
        public IReadOnlyList<ChartHubServerDownloadJobResponse> Jobs { get; set; } = [];
        public List<Guid> CancelledJobIds { get; } = [];
        public List<Guid> InstallRequestedJobIds { get; } = [];
        public IReadOnlyList<IReadOnlyList<ChartHubServerDownloadProgressEvent>> StreamBatches { get; set; } = [];
        public Action<IReadOnlyList<ChartHubServerDownloadProgressEvent>>? OnStreamBatch { get; set; }

        public Task<ChartHubServerAuthExchangeResponse> ExchangeGoogleTokenAsync(string baseUrl, string googleIdToken, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChartHubServerAuthExchangeResponse("token", DateTimeOffset.UtcNow.AddHours(1)));

        public Task<ChartHubServerDownloadJobResponse> CreateDownloadJobAsync(string baseUrl, string bearerToken, ChartHubServerCreateDownloadJobRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChartHubServerDownloadJobResponse(
                Guid.NewGuid(),
                request.Source,
                request.SourceId,
                request.DisplayName,
                request.SourceUrl,
                "Queued",
                0,
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));

        public Task<IReadOnlyList<ChartHubServerDownloadJobResponse>> ListDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => Task.FromResult(Jobs);

        public async IAsyncEnumerable<IReadOnlyList<ChartHubServerDownloadProgressEvent>> StreamDownloadJobsAsync(
            string baseUrl,
            string bearerToken,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (IReadOnlyList<ChartHubServerDownloadProgressEvent> batch in StreamBatches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                OnStreamBatch?.Invoke(batch);
                yield return batch;
                await Task.Yield();
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public Task RequestCancelDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
        {
            CancelledJobIds.Add(jobId);
            return Task.CompletedTask;
        }

        public Task<ChartHubServerDownloadJobResponse> RequestInstallDownloadJobAsync(
            string baseUrl,
            string bearerToken,
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            InstallRequestedJobIds.Add(jobId);
            ChartHubServerDownloadJobResponse existing = Jobs.FirstOrDefault(job => job.JobId == jobId)
                ?? new ChartHubServerDownloadJobResponse(
                    jobId,
                    LibrarySourceNames.RhythmVerse,
                    "source-id",
                    "display-name",
                    "https://example.test/download",
                    "Installing",
                    95,
                    null,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);

            return Task.FromResult(existing with { Stage = "Installing", ProgressPercent = Math.Max(existing.ProgressPercent, 95) });
        }
    }
}
