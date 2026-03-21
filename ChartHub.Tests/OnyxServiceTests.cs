using ChartHub.Configuration.Models;
using ChartHub.Configuration.Stores;
using ChartHub.Configuration.Interfaces;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;

namespace ChartHub.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public class OnyxServiceTests
{
    [Fact]
    public async Task InstallAsync_WhenImportSucceeds_UsesStagingAndOutputAndMovesFinalDirectory()
    {
        using var temp = new TemporaryDirectoryFixture("onyx-success");
        var settings = CreateSettings(temp.RootPath);
        var songPath = Path.Combine(settings.DownloadDir, "my-song.con");
        File.WriteAllText(songPath, "dummy");

        var capturedCalls = new List<string[]>();

        var sut = CreateService(settings, async (args, cancellationToken) =>
        {
            capturedCalls.Add(args);

            if (args[0] == "import")
            {
                var importPath = args[3];
                Directory.CreateDirectory(importPath);
                WriteMinimalSongYaml(Path.Combine(importPath, "song.yml"));
                await Task.CompletedTask;
                return;
            }

            if (args[0] == "build")
            {
                var buildPath = args[5];
                var builtSongDir = Path.Combine(buildPath, "Built Song");
                Directory.CreateDirectory(builtSongDir);
                File.WriteAllText(Path.Combine(builtSongDir, "notes.chart"), "chart-data");
            }
        });

        var result = await sut.InstallAsync(songPath, "rhythmverse");

        Assert.Equal(2, capturedCalls.Count);

        var importArgs = capturedCalls[0];
        Assert.Equal("import", importArgs[0]);
        Assert.Equal(songPath, importArgs[1]);
        Assert.Equal("--to", importArgs[2]);
        Assert.StartsWith(settings.StagingDir, importArgs[3], StringComparison.Ordinal);

        var buildArgs = capturedCalls[1];
        Assert.Equal("build", buildArgs[0]);
        Assert.EndsWith("song.yml", buildArgs[1]);
        Assert.Equal("--target", buildArgs[2]);
        Assert.Equal("ps", buildArgs[3]);
        Assert.Equal("--to", buildArgs[4]);
        Assert.StartsWith(settings.OutputDir, buildArgs[5], StringComparison.Ordinal);

        Assert.StartsWith(settings.CloneHeroSongsDir, result.FinalInstallDirectory, StringComparison.Ordinal);
        Assert.Contains($"{Path.DirectorySeparatorChar}Test Artist{Path.DirectorySeparatorChar}", result.FinalInstallDirectory, StringComparison.Ordinal);
        Assert.Contains($"{Path.DirectorySeparatorChar}Test Song{Path.DirectorySeparatorChar}", result.FinalInstallDirectory, StringComparison.Ordinal);
        Assert.Contains("__rhythmverse", result.FinalInstallDirectory, StringComparison.Ordinal);
        Assert.Equal("Test Artist", result.ParsedMetadata.Artist);
        Assert.Equal("Test Song", result.ParsedMetadata.Title);
        Assert.Equal("Unknown Charter", result.ParsedMetadata.Charter);
        Assert.True(Directory.Exists(result.FinalInstallDirectory));
        Assert.True(File.Exists(Path.Combine(result.FinalInstallDirectory, "notes.chart")));
    }

    [Fact]
    public async Task InstallAsync_WhenSongYmlAbsentAfterImport_ThrowsWithoutBuild()
    {
        using var temp = new TemporaryDirectoryFixture("onyx-no-yml");
        var settings = CreateSettings(temp.RootPath);
        var songPath = Path.Combine(settings.DownloadDir, "my-song.con");
        File.WriteAllText(songPath, "dummy");

        var capturedCalls = new List<string[]>();

        var sut = CreateService(settings, (args, cancellationToken) =>
        {
            capturedCalls.Add(args);
            if (args[0] == "import")
                Directory.CreateDirectory(args[3]);

            return Task.CompletedTask;
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InstallAsync(songPath, "rhythmverse"));

        Assert.Single(capturedCalls);
        Assert.Equal("import", capturedCalls[0][0]);
        Assert.Contains("song.yml", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallAsync_WhenRunOnyxThrows_ExceptionPropagates()
    {
        using var temp = new TemporaryDirectoryFixture("onyx-fail");
        var settings = CreateSettings(temp.RootPath);
        var songPath = Path.Combine(settings.DownloadDir, "broken.con");
        File.WriteAllText(songPath, "dummy");

        var sut = CreateService(settings, (args, cancellationToken) =>
            throw new InvalidOperationException("Onyx exited with code 1: import failed"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InstallAsync(songPath, "rhythmverse"));
        Assert.Contains("import failed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallAsync_WhenCancelled_ReportsCancelledStageAndThrowsOperationCanceled()
    {
        using var temp = new TemporaryDirectoryFixture("onyx-cancel");
        var settings = CreateSettings(temp.RootPath);
        var songPath = Path.Combine(settings.DownloadDir, "cancel.con");
        File.WriteAllText(songPath, "dummy");

        var progressUpdates = new List<InstallProgressUpdate>();
        var progress = new Progress<InstallProgressUpdate>(update => progressUpdates.Add(update));
        var cts = new CancellationTokenSource();

        var sut = CreateService(settings, async (args, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        });

        var installTask = sut.InstallAsync(songPath, "rhythmverse", progress, cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installTask);
        Assert.Contains(progressUpdates, update => update.Stage == InstallStage.Cancelled);
    }

    private static AppGlobalSettings CreateSettings(string rootPath)
    {
        var config = new AppConfigRoot
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

        foreach (var dir in new[]
        {
            config.Runtime.TempDirectory, config.Runtime.DownloadDirectory,
            config.Runtime.StagingDirectory, config.Runtime.OutputDirectory,
            config.Runtime.CloneHeroDataDirectory, config.Runtime.CloneHeroSongDirectory,
        })
            Directory.CreateDirectory(dir);

        return new AppGlobalSettings(new FakeSettingsOrchestrator(config));
    }

    private static OnyxService CreateService(AppGlobalSettings settings, Func<string[], CancellationToken, Task> runOnyx)
    {
        var constructor = typeof(OnyxService).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [typeof(AppGlobalSettings), typeof(Func<string[], CancellationToken, Task>)],
            modifiers: null);

        Assert.NotNull(constructor);

        return (OnyxService)constructor.Invoke([settings, runOnyx]);
    }

    private static void WriteMinimalSongYaml(string path)
    {
        File.WriteAllText(path, """
            targets:
              rb3:
                game: rb3
            metadata:
              artist: Test Artist
              title: Test Song
            """);
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
