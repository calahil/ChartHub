using ChartHub.Services;
using ChartHub.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class AuthFlowViewModelTests
{
    [Fact]
    public async Task AuthGateViewModel_SuccessfulSignIn_InvokesAuthenticatedCallback()
    {
        var cloudAccountService = new FakeCloudStorageAccountService
        {
            ProviderDisplayName = "Google Drive",
        };
        int callbackCount = 0;
        var sut = new AuthGateViewModel(cloudAccountService, () =>
        {
            callbackCount++;
            return Task.CompletedTask;
        });

        await sut.SignInCommand.ExecuteAsync(null);

        Assert.Equal(1, cloudAccountService.LinkCallCount);
        Assert.Equal(1, callbackCount);
        Assert.False(sut.IsBusy);
        Assert.Null(sut.ErrorMessage);
        Assert.Equal("Google Drive connected.", sut.StatusMessage);
    }

    [Fact]
    public void AuthGateViewModel_UsesProviderDisplayNameInUserFacingCopy()
    {
        var cloudAccountService = new FakeCloudStorageAccountService
        {
            ProviderDisplayName = "Acme Cloud",
        };

        var sut = new AuthGateViewModel(cloudAccountService, () => Task.CompletedTask);

        Assert.Equal("Acme Cloud", sut.ProviderDisplayName);
        Assert.Equal("Sign in with Acme Cloud to use cloud storage as your sync backend.", sut.DescriptionText);
        Assert.Equal("Sign In With Acme Cloud", sut.SignInButtonText);
        Assert.Equal("Sign in to Acme Cloud to enable synced storage.", sut.StatusMessage);
    }

    [Fact]
    public async Task AuthGateViewModel_FailedSignIn_SetsUserFacingErrorAndResetsBusy()
    {
        var cloudAccountService = new FakeCloudStorageAccountService
        {
            LinkException = new InvalidOperationException("network unavailable"),
            ProviderDisplayName = "Google Drive",
        };
        int callbackCount = 0;
        var sut = new AuthGateViewModel(cloudAccountService, () =>
        {
            callbackCount++;
            return Task.CompletedTask;
        });

        await sut.SignInCommand.ExecuteAsync(null);

        Assert.Equal(1, cloudAccountService.LinkCallCount);
        Assert.Equal(0, callbackCount);
        Assert.False(sut.IsBusy);
        Assert.Equal("Cloud sign-in failed: network unavailable", sut.ErrorMessage);
        Assert.Equal("Sign in to Google Drive to enable synced storage.", sut.StatusMessage);
    }

    [Fact]
    public async Task AppShellViewModel_WhenSilentSignInFails_ShowsMainShellWithoutAuthGate()
    {
        var cloudAccountService = new FakeCloudStorageAccountService
        {
            TryRestoreSessionResult = false,
        };
        var mainViewModel = new MainViewModel();
        var serviceProvider = new SingleServiceProvider(typeof(MainViewModel), mainViewModel);
        var sut = new AppShellViewModel(serviceProvider, cloudAccountService);

        SplashViewModel splash = Assert.IsType<SplashViewModel>(sut.CurrentViewModel);
        Assert.False(sut.IsSignedIn);

        await splash.RunAsync();

        Assert.Equal(1, cloudAccountService.TryRestoreSessionCallCount);
        Assert.False(sut.IsSignedIn);
        Assert.Same(mainViewModel, sut.CurrentViewModel);
    }

    [Fact]
    public async Task AppShellViewModel_WhenSilentSignInSucceeds_SkipsAuthGate()
    {
        var cloudAccountService = new FakeCloudStorageAccountService
        {
            TryRestoreSessionResult = true,
        };
        var mainViewModel = new MainViewModel();
        var serviceProvider = new SingleServiceProvider(typeof(MainViewModel), mainViewModel);
        var sut = new AppShellViewModel(serviceProvider, cloudAccountService);

        SplashViewModel splash = Assert.IsType<SplashViewModel>(sut.CurrentViewModel);
        await splash.RunAsync();

        Assert.Equal(1, cloudAccountService.TryRestoreSessionCallCount);
        Assert.True(sut.IsSignedIn);
        Assert.Same(mainViewModel, sut.CurrentViewModel);
    }

    private sealed class FakeCloudStorageAccountService : ICloudStorageAccountService
    {
        public bool TryRestoreSessionResult { get; set; }
        public Exception? LinkException { get; set; }

        public int TryRestoreSessionCallCount { get; private set; }
        public int LinkCallCount { get; private set; }
        public int UnlinkCallCount { get; private set; }

        public string ProviderId => "test-provider";
        public string ProviderDisplayName { get; set; } = "Test Provider";

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
        {
            TryRestoreSessionCallCount++;
            return Task.FromResult(TryRestoreSessionResult);
        }

        public Task LinkAsync(CancellationToken cancellationToken = default)
        {
            LinkCallCount++;
            if (LinkException is not null)
            {
                throw LinkException;
            }

            return Task.CompletedTask;
        }

        public Task UnlinkAsync(CancellationToken cancellationToken = default)
        {
            UnlinkCallCount++;
            return Task.CompletedTask;
        }
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

}
