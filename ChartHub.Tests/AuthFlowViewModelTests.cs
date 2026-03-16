using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Services.Transfers;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class AuthFlowViewModelTests
{
    [Fact]
    public async Task AuthGateViewModel_SuccessfulSignIn_InvokesAuthenticatedCallback()
    {
        var driveClient = new FakeGoogleDriveClient();
        var callbackCount = 0;
        var sut = new AuthGateViewModel(driveClient, () =>
        {
            callbackCount++;
            return Task.CompletedTask;
        });

        await sut.SignInCommand.ExecuteAsync(null);

        Assert.Equal(1, driveClient.InitializeCallCount);
        Assert.Equal(1, callbackCount);
        Assert.False(sut.IsBusy);
        Assert.Null(sut.ErrorMessage);
        Assert.Equal("Google Drive connected.", sut.StatusMessage);
    }

    [Fact]
    public async Task AuthGateViewModel_FailedSignIn_SetsUserFacingErrorAndResetsBusy()
    {
        var driveClient = new FakeGoogleDriveClient
        {
            InitializeException = new InvalidOperationException("network unavailable"),
        };
        var callbackCount = 0;
        var sut = new AuthGateViewModel(driveClient, () =>
        {
            callbackCount++;
            return Task.CompletedTask;
        });

        await sut.SignInCommand.ExecuteAsync(null);

        Assert.Equal(1, driveClient.InitializeCallCount);
        Assert.Equal(0, callbackCount);
        Assert.False(sut.IsBusy);
        Assert.Equal("Google sign-in failed: network unavailable", sut.ErrorMessage);
        Assert.Equal("Sign in to Google Drive to enable synced storage.", sut.StatusMessage);
    }

    [Fact]
    public async Task AppShellViewModel_SignInThenSignOut_TransitionsBetweenGateAndMain()
    {
        var driveClient = new FakeGoogleDriveClient();
        var mainViewModel = new MainViewModel();
        var serviceProvider = new SingleServiceProvider(typeof(MainViewModel), mainViewModel);
        var sut = new AppShellViewModel(serviceProvider, driveClient);

        Assert.IsType<AuthGateViewModel>(sut.CurrentViewModel);
        Assert.False(sut.IsSignedIn);

        var authGate = Assert.IsType<AuthGateViewModel>(sut.CurrentViewModel);
        await authGate.SignInCommand.ExecuteAsync(null);

        Assert.True(sut.IsSignedIn);
        Assert.Same(mainViewModel, sut.CurrentViewModel);

        await sut.SignOutCommand.ExecuteAsync(null);

        Assert.Equal(1, driveClient.SignOutCallCount);
        Assert.False(sut.IsSignedIn);
        Assert.IsType<AuthGateViewModel>(sut.CurrentViewModel);
    }

    private sealed class SingleServiceProvider(Type serviceType, object instance) : IServiceProvider
    {
        private readonly Type _serviceType = serviceType;
        private readonly object _instance = instance;

        public object? GetService(Type serviceType)
        {
            return serviceType == _serviceType ? _instance : null;
        }
    }

    private sealed class FakeGoogleDriveClient : IGoogleDriveClient
    {
        public Exception? InitializeException { get; set; }

        public int InitializeCallCount { get; private set; }
        public int SignOutCallCount { get; private set; }

        public string ChartHubFolderId => "folder-test";

        public Task<string> CreateDirectoryAsync(string directoryName) => Task.FromResult("folder-test");
        public Task<string> GetDirectoryIdAsync(string directoryName) => Task.FromResult("folder-test");
        public Task<string> UploadFileAsync(string directoryId, string filePath, string? desiredFileName = null) => Task.FromResult("file-test");
        public Task<string> CopyFileIntoFolderAsync(string sourceFileId, string destinationFolderId, string desiredFileName) => Task.FromResult("copy-test");
        public Task DownloadFolderAsZipAsync(string folderId, string zipFilePath, IProgress<TransferProgressUpdate>? stageProgress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DownloadFileAsync(string fileId, string saveToPath) => Task.CompletedTask;
        public Task DeleteFileAsync(string fileId) => Task.CompletedTask;
        public Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesAsync(string directoryId) => Task.FromResult<IList<Google.Apis.Drive.v3.Data.File>>(new List<Google.Apis.Drive.v3.Data.File>());
        public Task MonitorDirectoryAsync(string directoryId, TimeSpan pollingInterval, Action<Google.Apis.Drive.v3.Data.File, string> onFileChanged, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ObservableCollection<WatcherFile>> GetFileDataCollectionAsync(string directoryId) => Task.FromResult(new ObservableCollection<WatcherFile>());

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCallCount++;
            if (InitializeException is not null)
                throw InitializeException;

            return Task.CompletedTask;
        }

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            SignOutCallCount++;
            return Task.CompletedTask;
        }
    }
}
