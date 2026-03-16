using Microsoft.Extensions.DependencyInjection;
using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Migration;
using ChartHub.Configuration.Stores;
using ChartHub.Services;
using ChartHub.Services.Transfers;
using ChartHub.Utilities;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.IntegrationLite)]
public class AppBootstrapperTests
{
    [Fact]
    public void CreateServiceProvider_WithTempConfig_ResolvesCoreServices()
    {
        using var temp = new TestInfrastructure.TemporaryDirectoryFixture("bootstrapper-resolve");

        var provider = CreateProvider(temp.RootPath, migrationActionOverride: null);

        Assert.NotNull(provider.GetService<IAppConfigStore>());
        Assert.NotNull(provider.GetService<ISettingsOrchestrator>());
        Assert.NotNull(provider.GetService<AppGlobalSettings>());
        Assert.NotNull(provider.GetService<ITransferOrchestrator>());
        Assert.NotNull(provider.GetService<IGoogleDriveClient>());
    }

    [Fact]
    public void CreateServiceProvider_WhenMigrationThrows_DoesNotCrashAndStillResolvesServices()
    {
        using var temp = new TestInfrastructure.TemporaryDirectoryFixture("bootstrapper-migration-fail");

        var provider = CreateProvider(
            temp.RootPath,
            _ => throw new InvalidOperationException("migration boom"));

        Assert.NotNull(provider.GetService<SettingsMigrationService>());
        Assert.NotNull(provider.GetService<AppGlobalSettings>());
    }

    private static IServiceProvider CreateProvider(string configDir, Func<IServiceProvider, Task>? migrationActionOverride)
    {
        var method = typeof(AppBootstrapper).GetMethod(
            "CreateServiceProvider",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [typeof(string), typeof(Func<IServiceProvider, Task>)],
            modifiers: null);

        Assert.NotNull(method);

        return (IServiceProvider)method.Invoke(null, [configDir, migrationActionOverride])!;
    }
}
