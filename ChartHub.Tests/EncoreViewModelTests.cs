using System.Collections.ObjectModel;
using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Services.Transfers;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class EncoreViewModelTests
{
    [Fact]
    public async Task RefreshAsync_WithAdvancedFields_UsesAdvancedEndpoint()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-advanced");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.enchor.us"),
        };

        var api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpTransferOrchestrator(), catalog, new NoOpSettingsOrchestrator(), new SharedDownloadQueue())
        {
            AdvancedName = "Song",
            AdvancedAlbum = "Album",
            MinYear = "2000",
            MaxYear = "2020",
            HasIssues = true,
            Modchart = false,
        };

        await sut.RefreshAsync();

        Assert.Contains(handler.RequestUris, uri => uri.Contains("/search/advanced", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAsync_WithoutAdvancedFields_UsesGeneralSearchEndpoint()
    {
        using var temp = new TemporaryDirectoryFixture("encore-vm-general");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var handler = new RecordingHttpHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.enchor.us"),
        };

        var api = CreateApiService(catalog, httpClient);
        var sut = new EncoreViewModel(api, new NoOpTransferOrchestrator(), catalog, new NoOpSettingsOrchestrator(), new SharedDownloadQueue());

        await sut.RefreshAsync();

        Assert.Contains(handler.RequestUris, uri => uri.Contains("/search", StringComparison.Ordinal) && !uri.Contains("/search/advanced", StringComparison.Ordinal));
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            var json = """
            {
              "found": 0,
              "out_of": 0,
              "page": 1,
              "search_time_ms": 0,
              "data": []
            }
            """;

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            });
        }
    }

    private static EncoreApiService CreateApiService(LibraryCatalogService catalog, HttpClient httpClient)
    {
        var constructor = typeof(EncoreApiService).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [typeof(LibraryCatalogService), typeof(HttpClient)],
            modifiers: null);

        Assert.NotNull(constructor);
        return (EncoreApiService)constructor!.Invoke([catalog, httpClient]);
    }

    private sealed class NoOpTransferOrchestrator : ITransferOrchestrator
    {
        public Task<TransferResult> QueueSongDownloadAsync(ViewSong song, DownloadFile? downloadItem, ObservableCollection<DownloadFile> downloads, CancellationToken cancellationToken = default)
        {
            var item = downloadItem ?? new DownloadFile(song.FileName ?? "song.sng", Path.GetTempPath(), song.DownloadLink ?? string.Empty, song.FileSize)
            {
                Finished = true,
                Status = TransferStage.Completed.ToString(),
                DownloadProgress = 100,
            };
            return Task.FromResult(new TransferResult(true, TransferStage.Completed, song.FileName, null, item));
        }

        public Task<IReadOnlyList<string>> DownloadSelectedCloudFilesToLocalAsync(IEnumerable<WatcherFile> selectedCloudFiles, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> SyncCloudToLocalAdditiveAsync(IEnumerable<WatcherFile> currentCloudFiles, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class NoOpSettingsOrchestrator : ISettingsOrchestrator
    {
        public AppConfigRoot Current { get; private set; } = new();
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
}
