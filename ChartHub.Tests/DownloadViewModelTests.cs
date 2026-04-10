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
        using AppGlobalSettings settings = CreateSettings(temp.RootPath);
        var sut = new ViewModels.DownloadViewModel(
            settings,
            new SongInstallServiceStub(),
            new FakeChartHubServerApiClient(),
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
        using AppGlobalSettings settings = CreateSettings(temp.RootPath);
        string selectedFilePath = temp.GetPath("song-a.zip");
        await File.WriteAllTextAsync(selectedFilePath, "zip");

        var installStub = new SongInstallServiceStub
        {
            ResultPaths = [Path.Combine(temp.RootPath, "CloneHero", "Songs", "Song A")],
        };

        var sut = new ViewModels.DownloadViewModel(
            settings,
            installStub,
            new FakeChartHubServerApiClient(),
            uiInvoke: action =>
            {
                action();
                return Task.CompletedTask;
            });

        try
        {
            sut.IngestionQueue.Add(new IngestionQueueItem
            {
                IngestionId = 1,
                DisplayName = "song-a.zip",
                CurrentState = IngestionState.Downloaded,
                DownloadedLocation = selectedFilePath,
                Checked = true,
            });

            await sut.InstallSongsCommand();

            Assert.Equal("Installed 1 item successfully.", sut.InstallSummaryText);
            Assert.True(sut.HasInstallSummary);
            Assert.True(sut.IsInstallPanelVisible);
            Assert.False(sut.IsInstallActive);

            sut.DismissInstallPanelCommand.Execute(null);

            Assert.False(sut.IsInstallPanelVisible);
            Assert.False(sut.HasInstallSummary);
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

        var sut = new ViewModels.DownloadViewModel(
            settings,
            new SongInstallServiceStub(),
            fakeClient,
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
                TempDirectory = Path.Combine(rootPath, "Temp"),
                DownloadDirectory = Path.Combine(rootPath, "Downloads"),
                StagingDirectory = Path.Combine(rootPath, "Staging"),
                OutputDirectory = Path.Combine(rootPath, "Output"),
                CloneHeroDataDirectory = Path.Combine(rootPath, "CloneHero"),
                CloneHeroSongDirectory = Path.Combine(rootPath, "CloneHero", "Songs"),
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

    private sealed class SongInstallServiceStub : ISongInstallService
    {
        public IReadOnlyList<string> ResultPaths { get; set; } = [];

        public Task<IReadOnlyList<string>> InstallSelectedDownloadsAsync(IEnumerable<string> selectedFilePaths, IProgress<InstallProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ResultPaths);
        }
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

        public Task RequestCancelDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
        {
            CancelledJobIds.Add(jobId);
            return Task.CompletedTask;
        }
    }
}
