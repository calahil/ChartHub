using System.Collections.ObjectModel;
using System.Reflection;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Services.Transfers;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;

using Google.Apis.Auth.OAuth2;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class TransferOrchestratorTests
{
    [Fact]
    public async Task QueueSongDownloadAsync_InvalidMetadata_FailsWithoutThrowing()
    {
        using var temp = new TemporaryDirectoryFixture("transfer-invalid");
        TransferOrchestrator sut = CreateOrchestrator(temp.RootPath, new ResolverSuccessStub());
        var downloads = new ObservableCollection<DownloadFile>();
        var song = new ViewSong
        {
            FileName = string.Empty,
            DownloadLink = string.Empty,
            FileSize = 123,
        };

        TransferResult result = await sut.QueueSongDownloadAsync(song, null, downloads);

        Assert.False(result.Success);
        Assert.Equal(TransferStage.Failed, result.FinalStage);
        Assert.Equal("Song metadata is missing file name or source URL.", result.Error);
        Assert.True(result.DownloadItem.Finished);
        Assert.Empty(downloads);
    }

    [Fact]
    public async Task QueueSongDownloadAsync_WhenResolverCancels_ReturnsCancelled()
    {
        using var temp = new TemporaryDirectoryFixture("transfer-cancel");
        TransferOrchestrator sut = CreateOrchestrator(temp.RootPath, new ResolverCancelledStub());
        var downloads = new ObservableCollection<DownloadFile>();
        var song = new ViewSong
        {
            FileName = "song.zip",
            DownloadLink = "https://example.test/song.zip",
            FileSize = 100,
            SourceName = LibrarySourceNames.RhythmVerse,
            SourceId = "song-cancel",
        };

        TransferResult result = await sut.QueueSongDownloadAsync(song, null, downloads);

        Assert.False(result.Success);
        Assert.Equal(TransferStage.Cancelled, result.FinalStage);
        Assert.Equal("Transfer cancelled.", result.Error);
        Assert.True(result.DownloadItem.Finished);
        Assert.Equal(TransferStage.Cancelled.ToString(), result.DownloadItem.Status);
        Assert.Equal("Transfer cancelled.", result.DownloadItem.ErrorMessage);
        Assert.Single(downloads);
    }

    [Fact]
    public async Task QueueSongDownloadAsync_WhenResolverThrows_ReturnsFailedWithUserSafeMessage()
    {
        using var temp = new TemporaryDirectoryFixture("transfer-fail");
        TransferOrchestrator sut = CreateOrchestrator(temp.RootPath, new ResolverFailureStub());
        var downloads = new ObservableCollection<DownloadFile>();
        var song = new ViewSong
        {
            FileName = "song.zip",
            DownloadLink = "https://example.test/song.zip",
            FileSize = 100,
            SourceName = LibrarySourceNames.RhythmVerse,
            SourceId = "song-fail",
        };

        TransferResult result = await sut.QueueSongDownloadAsync(song, null, downloads);

        Assert.False(result.Success);
        Assert.Equal(TransferStage.Failed, result.FinalStage);
        Assert.Equal("Transfer failed. See logs for details.", result.Error);
        Assert.True(result.DownloadItem.Finished);
        Assert.Equal(TransferStage.Failed.ToString(), result.DownloadItem.Status);
        Assert.Equal("Transfer failed. See logs for details.", result.DownloadItem.ErrorMessage);
        Assert.Single(downloads);
    }

    [Fact]
    public async Task QueueSongDownloadAsync_GoogleFolderSource_CompletesLocalDestination()
    {
        using var temp = new TemporaryDirectoryFixture("transfer-local-success");
        string settingsRoot = temp.CreateSubdirectory("settings-root");
        string localDestinationRoot = temp.CreateSubdirectory("local-destination");
        TransferOrchestrator sut = CreateOrchestrator(
            settingsRoot,
            new ResolverGoogleFolderStub("folder-123"),
            new GoogleDriveClientCreatesZipStub(),
            new LocalDestinationWriterSuccessStub(localDestinationRoot));

        var downloads = new ObservableCollection<DownloadFile>();
        var song = new ViewSong
        {
            FileName = "song.bundle",
            DownloadLink = "https://drive.google.com/drive/folders/folder-123",
            FileSize = 100,
            SourceName = LibrarySourceNames.RhythmVerse,
            SourceId = "song-folder",
        };

        TransferResult result = await sut.QueueSongDownloadAsync(song, null, downloads);

        Assert.True(result.Success);
        Assert.Equal(TransferStage.Completed, result.FinalStage);
        Assert.NotNull(result.FinalLocation);
        Assert.True(File.Exists(result.FinalLocation));
        Assert.True(
            result.DownloadItem.Status == TransferStage.Completed.ToString()
            || result.DownloadItem.Status == TransferStage.ZippingFolder.ToString(),
            $"Unexpected download status '{result.DownloadItem.Status}'.");
        Assert.Equal(localDestinationRoot, result.DownloadItem.FilePath);
        Assert.Single(downloads);
    }

    [Fact]
    public async Task TryCopyDriveFileAsync_WhenCopySucceeds_ReturnsCompletedResult()
    {
        using var temp = new TemporaryDirectoryFixture("transfer-copy-success");
        var destinationWriter = new GoogleDriveDestinationWriterCopySuccessStub();
        TransferOrchestrator sut = CreateOrchestrator(
            temp.RootPath,
            new ResolverSuccessStub(),
            googleDriveDestinationWriter: destinationWriter);

        var request = new TransferRequest(
            DisplayName: "song.zip",
            SourceUrl: "https://drive.google.com/file/d/file-123/view",
            SourceFileSize: 100,
            Destination: TransferDestinationKind.GoogleDrive);
        var source = new ResolvedTransferSource(
            OriginalUrl: request.SourceUrl,
            FinalUrl: request.SourceUrl,
            Kind: TransferSourceKind.GoogleDriveFile,
            DriveId: "file-123");
        var downloadItem = new DownloadFile("song.zip", temp.RootPath, request.SourceUrl, 100);

        TransferResult? result = await InvokeTryCopyDriveFileAsync(sut, request, source, downloadItem);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal(TransferStage.Completed, result.FinalStage);
        Assert.Equal("copied-file-id", result.FinalLocation);
        Assert.Equal("song.zip", downloadItem.DisplayName);
        Assert.Equal("folder-test", downloadItem.FilePath);
        Assert.Equal(100, downloadItem.DownloadProgress);
        Assert.True(downloadItem.Finished);
        Assert.Equal(TransferStage.Completed.ToString(), downloadItem.Status);
    }

    [Fact]
    public async Task TryCopyDriveFileAsync_WhenCopyThrows_ReturnsNullForFallback()
    {
        using var temp = new TemporaryDirectoryFixture("transfer-copy-fallback");
        var destinationWriter = new GoogleDriveDestinationWriterCopyThrowsStub();
        TransferOrchestrator sut = CreateOrchestrator(
            temp.RootPath,
            new ResolverSuccessStub(),
            googleDriveDestinationWriter: destinationWriter);

        var request = new TransferRequest(
            DisplayName: "song.zip",
            SourceUrl: "https://drive.google.com/file/d/file-123/view",
            SourceFileSize: 100,
            Destination: TransferDestinationKind.GoogleDrive);
        var source = new ResolvedTransferSource(
            OriginalUrl: request.SourceUrl,
            FinalUrl: request.SourceUrl,
            Kind: TransferSourceKind.GoogleDriveFile,
            DriveId: "file-123");
        var downloadItem = new DownloadFile("song.zip", temp.RootPath, request.SourceUrl, 100);

        TransferResult? result = await InvokeTryCopyDriveFileAsync(sut, request, source, downloadItem);

        Assert.Null(result);
        Assert.Equal(TransferStage.CopyingInGoogleDrive.ToString(), downloadItem.Status);
        Assert.False(downloadItem.Finished);
    }

    private static TransferOrchestrator CreateOrchestrator(
        string rootPath,
        ITransferSourceResolver resolver,
        IGoogleDriveClient? googleDriveClient = null,
        ILocalDestinationWriter? localDestinationWriter = null,
        IGoogleDriveDestinationWriter? googleDriveDestinationWriter = null)
    {
        var orchestrator = new FakeSettingsOrchestrator(rootPath);
        var settings = new AppGlobalSettings(orchestrator);

        return new TransferOrchestrator(
            settings,
            new DownloadService(new FakeGoogleAuthProvider()),
            googleDriveClient ?? new GoogleDriveClientStub(),
            resolver,
            localDestinationWriter ?? new LocalDestinationWriterStub(),
            googleDriveDestinationWriter ?? new GoogleDriveDestinationWriterStub(),
            new SongIngestionCatalogService(Path.Combine(rootPath, "library-catalog.db")),
            new SongIngestionStateMachine());
    }

    private static async Task<TransferResult?> InvokeTryCopyDriveFileAsync(
        TransferOrchestrator sut,
        TransferRequest request,
        ResolvedTransferSource source,
        DownloadFile downloadItem)
    {
        MethodInfo? method = typeof(TransferOrchestrator).GetMethod("TryCopyDriveFileAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = method!.Invoke(sut, [request, source, downloadItem, "trf-test", 0L, 0L, IngestionState.Queued, CancellationToken.None]) as Task<TransferResult?>;
        Assert.NotNull(task);

        return await task!;
    }

    private sealed class ResolverSuccessStub : ITransferSourceResolver
    {
        public Task<ResolvedTransferSource> ResolveAsync(string sourceUrl, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ResolvedTransferSource(sourceUrl, sourceUrl, TransferSourceKind.DirectHttp));
        }
    }

    private sealed class ResolverCancelledStub : ITransferSourceResolver
    {
        public Task<ResolvedTransferSource> ResolveAsync(string sourceUrl, CancellationToken cancellationToken = default)
        {
            throw new OperationCanceledException("cancelled for test");
        }
    }

    private sealed class ResolverFailureStub : ITransferSourceResolver
    {
        public Task<ResolvedTransferSource> ResolveAsync(string sourceUrl, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("resolver failure");
        }
    }

    private sealed class ResolverGoogleFolderStub(string driveId) : ITransferSourceResolver
    {
        private readonly string _driveId = driveId;

        public Task<ResolvedTransferSource> ResolveAsync(string sourceUrl, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ResolvedTransferSource(sourceUrl, sourceUrl, TransferSourceKind.GoogleDriveFolder, _driveId));
        }
    }

    private sealed class LocalDestinationWriterStub : ILocalDestinationWriter
    {
        public Task<DestinationWriteResult> WriteFromTempAsync(string tempFilePath, string desiredName, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public string ResolveUniqueName(string desiredName)
        {
            return desiredName;
        }
    }

    private sealed class GoogleDriveDestinationWriterStub : IGoogleDriveDestinationWriter
    {
        public Task<DestinationWriteResult> WriteFromTempAsync(string tempFilePath, string desiredName, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DestinationWriteResult?> TryCopyDriveFileAsync(string sourceFileId, string desiredName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DestinationWriteResult?>(null);
        }

        public Task<string> GetChartHubFolderIdAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult("folder-test");
        }
    }

    private sealed class GoogleDriveDestinationWriterCopySuccessStub : IGoogleDriveDestinationWriter
    {
        public Task<DestinationWriteResult> WriteFromTempAsync(string tempFilePath, string desiredName, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DestinationWriteResult?> TryCopyDriveFileAsync(string sourceFileId, string desiredName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DestinationWriteResult?>(new DestinationWriteResult(
                FinalName: desiredName,
                FinalLocation: "copied-file-id",
                DestinationContainer: "folder-test"));
        }

        public Task<string> GetChartHubFolderIdAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult("folder-test");
        }
    }

    private sealed class GoogleDriveDestinationWriterCopyThrowsStub : IGoogleDriveDestinationWriter
    {
        public Task<DestinationWriteResult> WriteFromTempAsync(string tempFilePath, string desiredName, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DestinationWriteResult?> TryCopyDriveFileAsync(string sourceFileId, string desiredName, CancellationToken cancellationToken = default)
        {
            throw new IOException("copy failed");
        }

        public Task<string> GetChartHubFolderIdAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult("folder-test");
        }
    }

    private class GoogleDriveClientStub : IGoogleDriveClient
    {
        public string ChartHubFolderId => "folder-test";

        public Task<string> CreateDirectoryAsync(string directoryName) => Task.FromResult("folder-test");
        public Task<string> GetDirectoryIdAsync(string directoryName) => Task.FromResult("folder-test");
        public Task<string> UploadFileAsync(string directoryId, string filePath, string? desiredFileName = null) => Task.FromResult("file-test");
        public Task<string> CopyFileIntoFolderAsync(string sourceFileId, string destinationFolderId, string desiredFileName) => Task.FromResult("copied-file");
        public virtual Task DownloadFolderAsZipAsync(string folderId, string zipFilePath, IProgress<TransferProgressUpdate>? stageProgress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DownloadFileAsync(string fileId, string saveToPath) => Task.CompletedTask;
        public Task DeleteFileAsync(string fileId) => Task.CompletedTask;
        public Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesAsync(string directoryId) => Task.FromResult<IList<Google.Apis.Drive.v3.Data.File>>(new List<Google.Apis.Drive.v3.Data.File>());
        public Task MonitorDirectoryAsync(string directoryId, TimeSpan pollingInterval, Action<Google.Apis.Drive.v3.Data.File, string> onFileChanged, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryInitializeSilentAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ObservableCollection<WatcherFile>> GetFileDataCollectionAsync(string directoryId) => Task.FromResult(new ObservableCollection<WatcherFile>());
    }

    private sealed class GoogleDriveClientCreatesZipStub : GoogleDriveClientStub
    {
        public override Task DownloadFolderAsZipAsync(string folderId, string zipFilePath, IProgress<TransferProgressUpdate>? stageProgress = null, CancellationToken cancellationToken = default)
        {
            string? directory = Path.GetDirectoryName(zipFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(zipFilePath, "zip payload");
            stageProgress?.Report(new TransferProgressUpdate(TransferStage.ZippingFolder, 100));
            return Task.CompletedTask;
        }
    }

    private sealed class LocalDestinationWriterSuccessStub(string destinationRoot) : ILocalDestinationWriter
    {
        private readonly string _destinationRoot = destinationRoot;

        public Task<DestinationWriteResult> WriteFromTempAsync(string tempFilePath, string desiredName, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(_destinationRoot);
            string finalPath = Path.Combine(_destinationRoot, desiredName);
            File.Move(tempFilePath, finalPath, overwrite: false);

            return Task.FromResult(new DestinationWriteResult(
                FinalName: desiredName,
                FinalLocation: finalPath,
                DestinationContainer: _destinationRoot));
        }

        public string ResolveUniqueName(string desiredName)
        {
            return desiredName;
        }
    }

    private sealed class FakeGoogleAuthProvider : IGoogleAuthProvider
    {
        public Task<UserCredential> AuthorizeInteractiveAsync(IEnumerable<string> scopes, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<UserCredential?> TryAuthorizeSilentAsync(IEnumerable<string> scopes, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<UserCredential?>(null);
        }

        public Task SignOutAsync(UserCredential? credential, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsOrchestrator : ISettingsOrchestrator
    {
        public AppConfigRoot Current { get; private set; }

        public event Action<AppConfigRoot>? SettingsChanged;

        public FakeSettingsOrchestrator(string rootPath)
        {
            Current = new AppConfigRoot
            {
                Runtime = new RuntimeAppConfig
                {
                    TempDirectory = Path.Combine(rootPath, "Temp"),
                    DownloadDirectory = Path.Combine(rootPath, "Downloads"),
                    StagingDirectory = Path.Combine(rootPath, "Staging"),
                    OutputDirectory = Path.Combine(rootPath, "Output"),
                    CloneHeroDataDirectory = Path.Combine(rootPath, "CloneHero"),
                    CloneHeroSongDirectory = Path.Combine(rootPath, "CloneHero", "Songs"),
                },
            };
        }

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
