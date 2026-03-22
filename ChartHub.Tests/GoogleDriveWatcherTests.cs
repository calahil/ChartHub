using System.Collections.ObjectModel;
using System.Reflection;

using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Services.Transfers;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class GoogleDriveWatcherTests
{
    [Fact]
    public async Task StartAsync_WhenFolderNotInitialized_RaisesDirectoryNotFound()
    {
        var driveClient = new SequencedGoogleDriveClientStub(string.Empty);
        var sut = new GoogleDriveWatcher(driveClient);

        string? observedMessage = null;
        sut.DirectoryNotFound += (_, message) => observedMessage = message;

        await sut.StartAsync();

        Assert.NotNull(observedMessage);
        Assert.Contains("not initialised", observedMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sut.Data);
    }

    [Fact]
    public async Task BuildMergedItemsAsync_SynchronizesAddedAndRemovedFiles()
    {
        var current = new List<WatcherFile>
        {
            new("alpha.zip", "file-a", WatcherFileType.Zip, "alpha.png", 10),
            new("beta.rar", "file-b", WatcherFileType.Rar, "beta.png", 20),
        };
        IList<Google.Apis.Drive.v3.Data.File> files = CreateFiles(("file-b", "beta.rar", 20), ("file-c", "gamma.7z", 30));

        IReadOnlyList<WatcherFile> merged = await InvokeBuildMergedItemsAsync(current, files);

        Assert.Equal(2, merged.Count);
        Assert.DoesNotContain(merged, item => item.FilePath == "file-a");
        Assert.Contains(merged, item => item.FilePath == "file-b" && item.FileType == WatcherFileType.Rar);
        Assert.Contains(merged, item => item.FilePath == "file-c" && item.FileType == WatcherFileType.SevenZip);
    }

    [Fact]
    public async Task PollAsync_WhenCancelledDuringDelay_ExitsCleanly()
    {
        var driveClient = new SequencedGoogleDriveClientStub("folder-123", Array.Empty<Google.Apis.Drive.v3.Data.File>());
        var sut = new GoogleDriveWatcher(driveClient)
        {
            PollingInterval = TimeSpan.FromSeconds(5),
        };

        using var cts = new CancellationTokenSource();
        Task pollTask = InvokePollAsync(sut, cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(25));

        await pollTask;

        Assert.True(pollTask.IsCompletedSuccessfully);
        Assert.Equal(0, driveClient.ListFilesCallCount);
    }

    private static IList<Google.Apis.Drive.v3.Data.File> CreateFiles(params (string Id, string Name, long Size)[] files)
    {
        return files.Select(file => new Google.Apis.Drive.v3.Data.File
        {
            Id = file.Id,
            Name = file.Name,
            Size = file.Size,
        }).ToList();
    }

    private static async Task<IReadOnlyList<WatcherFile>> InvokeBuildMergedItemsAsync(
        IEnumerable<WatcherFile> currentItems,
        IList<Google.Apis.Drive.v3.Data.File> files)
    {
        MethodInfo? method = typeof(GoogleDriveWatcher).GetMethod("BuildMergedItemsAsync", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = method!.Invoke(null, [currentItems, files]) as Task<IReadOnlyList<WatcherFile>>;
        Assert.NotNull(task);
        return await task!;
    }

    private static async Task InvokePollAsync(GoogleDriveWatcher watcher, CancellationToken cancellationToken)
    {
        MethodInfo? method = typeof(GoogleDriveWatcher).GetMethod("PollAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = method!.Invoke(watcher, [cancellationToken]) as Task;
        Assert.NotNull(task);
        await task!;
    }

    private sealed class SequencedGoogleDriveClientStub : IGoogleDriveClient
    {
        private readonly Queue<IList<Google.Apis.Drive.v3.Data.File>> _responses;

        public SequencedGoogleDriveClientStub(string folderId, params IList<Google.Apis.Drive.v3.Data.File>[] responses)
        {
            ChartHubFolderId = folderId;
            _responses = new Queue<IList<Google.Apis.Drive.v3.Data.File>>(responses);
        }

        public int ListFilesCallCount { get; private set; }

        public string ChartHubFolderId { get; }

        public Task<string> CreateDirectoryAsync(string directoryName) => Task.FromResult("folder-123");
        public Task<string> GetDirectoryIdAsync(string directoryName) => Task.FromResult("folder-123");
        public Task<string> UploadFileAsync(string directoryId, string filePath, string? desiredFileName = null) => Task.FromResult("file-123");
        public Task<string> CopyFileIntoFolderAsync(string sourceFileId, string destinationFolderId, string desiredFileName) => Task.FromResult("copy-123");
        public Task DownloadFolderAsZipAsync(string folderId, string zipFilePath, IProgress<TransferProgressUpdate>? stageProgress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DownloadFileAsync(string fileId, string saveToPath) => Task.CompletedTask;
        public Task DeleteFileAsync(string fileId) => Task.CompletedTask;
        public Task MonitorDirectoryAsync(string directoryId, TimeSpan pollingInterval, Action<Google.Apis.Drive.v3.Data.File, string> onFileChanged, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryInitializeSilentAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ObservableCollection<WatcherFile>> GetFileDataCollectionAsync(string directoryId) => Task.FromResult(new ObservableCollection<WatcherFile>());

        public Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesAsync(string directoryId)
        {
            ListFilesCallCount++;
            if (_responses.Count == 0)
            {
                return Task.FromResult<IList<Google.Apis.Drive.v3.Data.File>>(new List<Google.Apis.Drive.v3.Data.File>());
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
