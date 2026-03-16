using RhythmVerseClient.Configuration.Models;
using RhythmVerseClient.Configuration.Stores;
using RhythmVerseClient.Configuration.Interfaces;
using RhythmVerseClient.Services;
using RhythmVerseClient.Tests.TestInfrastructure;
using RhythmVerseClient.Utilities;

namespace RhythmVerseClient.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public class OnyxServiceTests
{
    [Fact]
    public void Constructor_WhenImportSucceeds_CallsImportThenBuildWithCorrectArgs()
    {
        using var temp = new TemporaryDirectoryFixture("onyx-success");
        var settings = CreateSettings(temp.RootPath);
        var songPath = "/songs/my-song.zip";

        var capturedCalls = new List<string[]>();

        _ = CreateService(settings, songPath, args =>
        {
            capturedCalls.Add(args);

            // On import call: create importPath dir and write a valid song.yml
            if (args[0] == "import")
            {
                var importPath = args[3];
                Directory.CreateDirectory(importPath);
                WriteMinimalSongYaml(Path.Combine(importPath, "song.yml"), settings.OutputDir);
            }
        });

        Assert.Equal(2, capturedCalls.Count);

        var importArgs = capturedCalls[0];
        Assert.Equal("import", importArgs[0]);
        Assert.Equal(songPath, importArgs[1]);
        Assert.Equal("--to", importArgs[2]);
        Assert.Equal(Path.GetFileName(songPath), Path.GetFileName(importArgs[3]));

        var buildArgs = capturedCalls[1];
        Assert.Equal("build", buildArgs[0]);
        Assert.EndsWith("song.yml", buildArgs[1]);
        Assert.Equal("--target", buildArgs[2]);
        Assert.Equal("ps", buildArgs[3]);
        Assert.Equal("--to", buildArgs[4]);
        Assert.Equal(settings.OutputDir, buildArgs[5]);
    }

    [Fact]
    public void Constructor_WhenSongYmlAbsentAfterImport_EarlyReturnsWithoutBuild()
    {
        using var temp = new TemporaryDirectoryFixture("onyx-no-yml");
        var settings = CreateSettings(temp.RootPath);
        var songPath = "/songs/my-song.zip";

        var capturedCalls = new List<string[]>();

        _ = CreateService(settings, songPath, args =>
        {
            capturedCalls.Add(args);
            // import stub: creates the directory but NOT the song.yml
            if (args[0] == "import")
                Directory.CreateDirectory(args[3]);
        });

        Assert.Single(capturedCalls);
        Assert.Equal("import", capturedCalls[0][0]);
    }

    [Fact]
    public void Constructor_WhenRunOnyxThrows_ExceptionPropagates()
    {
        using var temp = new TemporaryDirectoryFixture("onyx-fail");
        var settings = CreateSettings(temp.RootPath);
        var songPath = "/songs/broken.zip";

        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
        {
            _ = CreateService(settings, songPath, _ =>
                throw new InvalidOperationException("Onyx exited with code 1: import failed"));
        });

        Assert.NotNull(ex.InnerException);
        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("import failed", inner.Message, StringComparison.Ordinal);
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

    private static OnyxService CreateService(AppGlobalSettings settings, string songPath, Action<string[]> runOnyx)
    {
        var constructor = typeof(OnyxService).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [typeof(AppGlobalSettings), typeof(string), typeof(Action<string[]>)],
            modifiers: null);

        Assert.NotNull(constructor);

        return (OnyxService)constructor.Invoke([settings, songPath, runOnyx]);
    }

    private static void WriteMinimalSongYaml(string path, string outputDir)
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
