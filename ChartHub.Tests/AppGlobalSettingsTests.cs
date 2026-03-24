using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class AppGlobalSettingsTests
{
    [Fact]
    public async Task Constructor_WhenRuntimeDirectoriesAreFirstInstall_ResolvesAndCreatesLocalStorageDirectories()
    {
        AppConfigRoot config = new()
        {
            Runtime = new RuntimeAppConfig
            {
                TempDirectory = "first_install",
                DownloadDirectory = "first_install",
                StagingDirectory = "first_install",
                OutputDirectory = "first_install",
                CloneHeroDataDirectory = "first_install",
                CloneHeroSongDirectory = "first_install",
            },
        };

        using var settings = new AppGlobalSettings(new FakeSettingsOrchestrator(config));

        bool initialized = await WaitForConditionAsync(
            () => !string.IsNullOrWhiteSpace(settings.DownloadDir)
                && !string.Equals(settings.DownloadDir, "first_install", StringComparison.Ordinal),
            TimeSpan.FromSeconds(2));

        Assert.True(initialized);
        Assert.StartsWith(settings.TempDir, settings.DownloadDir, StringComparison.Ordinal);
        Assert.StartsWith(settings.TempDir, settings.StagingDir, StringComparison.Ordinal);
        Assert.StartsWith(settings.TempDir, settings.OutputDir, StringComparison.Ordinal);
        Assert.True(Directory.Exists(settings.TempDir));
        Assert.True(Directory.Exists(settings.DownloadDir));
        Assert.True(Directory.Exists(settings.StagingDir));
        Assert.True(Directory.Exists(settings.OutputDir));
    }

    [Fact]
    public async Task Constructor_WhenPairCodeExpired_RotatesCodeAndRefreshesIssuedAt()
    {
        using var temp = new TemporaryDirectoryFixture("app-global-settings-expired-pair-code");

        string expiredCode = "111111";
        DateTimeOffset expiredIssuedAt = DateTimeOffset.UtcNow.AddHours(-2);
        AppConfigRoot config = CreateConfig(temp.RootPath);
        config.Runtime.SyncApiPairCode = expiredCode;
        config.Runtime.SyncApiPairCodeIssuedAtUtc = expiredIssuedAt.ToString("O");
        config.Runtime.SyncApiPairCodeTtlMinutes = 10;

        using var settings = new AppGlobalSettings(new FakeSettingsOrchestrator(config));

        bool changed = await WaitForConditionAsync(
            () => settings.SyncApiPairCode != expiredCode,
            TimeSpan.FromSeconds(2));

        Assert.True(changed);
        Assert.Equal(6, settings.SyncApiPairCode.Length);
        Assert.True(DateTimeOffset.TryParse(settings.SyncApiPairCodeIssuedAtUtc, out DateTimeOffset refreshedIssuedAt));
        Assert.True(refreshedIssuedAt >= DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Constructor_WhenPairCodeStillValid_PreservesCodeAndIssuedAt()
    {
        using var temp = new TemporaryDirectoryFixture("app-global-settings-valid-pair-code");

        string validCode = "222222";
        DateTimeOffset issuedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        AppConfigRoot config = CreateConfig(temp.RootPath);
        config.Runtime.SyncApiPairCode = validCode;
        config.Runtime.SyncApiPairCodeIssuedAtUtc = issuedAt.ToString("O");
        config.Runtime.SyncApiPairCodeTtlMinutes = 10;

        using var settings = new AppGlobalSettings(new FakeSettingsOrchestrator(config));

        bool initialized = await WaitForConditionAsync(
            () => !string.IsNullOrWhiteSpace(settings.SyncApiPairCodeIssuedAtUtc),
            TimeSpan.FromSeconds(2));

        Assert.True(initialized);
        Assert.Equal(validCode, settings.SyncApiPairCode);
        Assert.Equal(issuedAt.ToString("O"), settings.SyncApiPairCodeIssuedAtUtc);
    }

    [Fact]
    public async Task Constructor_WhenSyncApiMaxBodySizeIsLegacyDefault_UpliftsToModernDefault()
    {
        using var temp = new TemporaryDirectoryFixture("app-global-settings-sync-api-max-body-uplift");

        AppConfigRoot config = CreateConfig(temp.RootPath);
        config.Runtime.SyncApiMaxRequestBodyBytes = 64 * 1024;

        using var settings = new AppGlobalSettings(new FakeSettingsOrchestrator(config));

        bool changed = await WaitForConditionAsync(
            () => settings.SyncApiMaxRequestBodyBytes == 32 * 1024 * 1024,
            TimeSpan.FromSeconds(2));

        Assert.True(changed);
        Assert.Equal(32 * 1024 * 1024, settings.SyncApiMaxRequestBodyBytes);
    }

    private static AppConfigRoot CreateConfig(string rootPath)
    {
        string tempDirectory = Path.Combine(rootPath, "Temp");
        string downloadDirectory = Path.Combine(rootPath, "Downloads");
        string stagingDirectory = Path.Combine(rootPath, "Staging");
        string outputDirectory = Path.Combine(rootPath, "Output");
        string cloneHeroDataDirectory = Path.Combine(rootPath, "CloneHero");
        string cloneHeroSongDirectory = Path.Combine(cloneHeroDataDirectory, "Songs");

        Directory.CreateDirectory(tempDirectory);
        Directory.CreateDirectory(downloadDirectory);
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(cloneHeroDataDirectory);
        Directory.CreateDirectory(cloneHeroSongDirectory);

        return new AppConfigRoot
        {
            Runtime = new RuntimeAppConfig
            {
                TempDirectory = tempDirectory,
                DownloadDirectory = downloadDirectory,
                StagingDirectory = stagingDirectory,
                OutputDirectory = outputDirectory,
                CloneHeroDataDirectory = cloneHeroDataDirectory,
                CloneHeroSongDirectory = cloneHeroSongDirectory,
            },
        };
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

    private sealed class FakeSettingsOrchestrator(AppConfigRoot current) : ISettingsOrchestrator
    {
        public AppConfigRoot Current { get; private set; } = current;

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
