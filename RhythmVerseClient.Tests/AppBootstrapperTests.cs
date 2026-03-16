using Microsoft.Extensions.DependencyInjection;
using RhythmVerseClient.Configuration.Interfaces;
using RhythmVerseClient.Configuration.Migration;
using RhythmVerseClient.Configuration.Stores;
using RhythmVerseClient.Services;
using RhythmVerseClient.Services.Transfers;
using RhythmVerseClient.Utilities;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Tests;

[Trait(RhythmVerseClient.Tests.TestInfrastructure.TestCategories.Category, RhythmVerseClient.Tests.TestInfrastructure.TestCategories.IntegrationLite)]
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
