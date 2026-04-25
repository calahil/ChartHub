using System.ComponentModel;

using ChartHub.Services;
using ChartHub.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class AuthFlowViewModelTests
{
    [Fact]
    public async Task AppShellViewModel_AfterSplash_ShowsMainShell()
    {
        var mainViewModel = new MainViewModel();
        IServiceProvider serviceProvider = BuildServiceProvider(mainViewModel);
        var sut = new AppShellViewModel(serviceProvider);

        SplashViewModel splash = Assert.IsType<SplashViewModel>(sut.CurrentViewModel);

        await splash.RunAsync();

        Assert.Same(mainViewModel, sut.CurrentViewModel);
    }

    [Fact]
    public async Task AppShellViewModel_UsesProvidedMainViewModelInstance()
    {
        var mainViewModel = new MainViewModel();
        IServiceProvider serviceProvider = BuildServiceProvider(mainViewModel);
        var sut = new AppShellViewModel(serviceProvider);

        SplashViewModel splash = Assert.IsType<SplashViewModel>(sut.CurrentViewModel);
        await splash.RunAsync();

        Assert.Same(mainViewModel, sut.CurrentViewModel);
    }

    private static IServiceProvider BuildServiceProvider(MainViewModel mainViewModel)
    {
        var services = new ServiceCollection();
        services.AddSingleton(mainViewModel);
        services.AddSingleton<IAuthSessionService, FakeAuthSessionService>();
        return services.BuildServiceProvider();
    }

    private sealed class FakeAuthSessionService : IAuthSessionService
    {
        public AuthSessionState CurrentState => AuthSessionState.Unauthenticated;
        public string? SignedInEmail => null;
        public string? CurrentAccessToken => null;
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? SessionStateChanged
        {
            add { }
            remove { }
        }

        public Task AttemptSilentRestoreAsync() => Task.CompletedTask;
        public Task SignInAsync() => Task.CompletedTask;
        public Task SignOutAsync() => Task.CompletedTask;
        public bool IsTokenValidLocally() => false;
        public Task AttemptSilentRefreshAsync() => Task.CompletedTask;
    }
}

