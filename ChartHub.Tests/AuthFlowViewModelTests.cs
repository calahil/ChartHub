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
        var serviceProvider = new SingleServiceProvider(typeof(MainViewModel), mainViewModel);
        var sut = new AppShellViewModel(serviceProvider);

        SplashViewModel splash = Assert.IsType<SplashViewModel>(sut.CurrentViewModel);

        await splash.RunAsync();

        Assert.Same(mainViewModel, sut.CurrentViewModel);
    }

    [Fact]
    public async Task AppShellViewModel_UsesProvidedMainViewModelInstance()
    {
        var mainViewModel = new MainViewModel();
        var serviceProvider = new SingleServiceProvider(typeof(MainViewModel), mainViewModel);
        var sut = new AppShellViewModel(serviceProvider);

        SplashViewModel splash = Assert.IsType<SplashViewModel>(sut.CurrentViewModel);
        await splash.RunAsync();

        Assert.Same(mainViewModel, sut.CurrentViewModel);
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
