using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Tests;

public sealed class UnityLaunchOptimizerTests
{
    // ── FindBootConfigPath ──────────────────────────────────────────────────

    [Fact]
    public void FindBootConfigPathReturnsNullWhenNoBinariesDataDirectory()
    {
        string root = CreateTempDirectory();
        try
        {
            string exe = Path.Combine(root, "game");
            File.WriteAllText(exe, string.Empty);

            string? result = UnityLaunchOptimizer.FindBootConfigPath(exe);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindBootConfigPathReturnsNullWhenDataDirLacksBootConfig()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "game_Data");
            Directory.CreateDirectory(dataDir);
            string exe = Path.Combine(root, "game");
            File.WriteAllText(exe, string.Empty);

            string? result = UnityLaunchOptimizer.FindBootConfigPath(exe);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindBootConfigPathReturnsPathWhenBootConfigPresent()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "mygame_Data");
            Directory.CreateDirectory(dataDir);
            string bootConfig = Path.Combine(dataDir, "boot.config");
            File.WriteAllText(bootConfig, string.Empty);
            string exe = Path.Combine(root, "mygame");

            string? result = UnityLaunchOptimizer.FindBootConfigPath(exe);

            Assert.Equal(bootConfig, result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── UpsertBootConfigKeys ────────────────────────────────────────────────

    [Fact]
    public void UpsertBootConfigKeysAddsNewKeysToEmptyContent()
    {
        var keys = new Dictionary<string, string>
        {
            ["gfx-enable-gfx-jobs"] = "1",
            ["gc-max-time-slice"] = "3",
        };

        string result = UnityLaunchOptimizer.UpsertBootConfigKeys(string.Empty, keys);

        Assert.Contains("gfx-enable-gfx-jobs=1", result);
        Assert.Contains("gc-max-time-slice=3", result);
    }

    [Fact]
    public void UpsertBootConfigKeysUpdatesExistingKeyWithWrongValue()
    {
        string content = "wait-for-native-debugger=0\nhdr-display-enabled=0\n";
        var keys = new Dictionary<string, string> { ["hdr-display-enabled"] = "1" };

        string result = UnityLaunchOptimizer.UpsertBootConfigKeys(content, keys);

        Assert.Contains("hdr-display-enabled=1", result);
        Assert.DoesNotContain("hdr-display-enabled=0", result);
    }

    [Fact]
    public void UpsertBootConfigKeysPreservesUnrelatedLines()
    {
        string content = "wait-for-native-debugger=0\nhdr-display-enabled=0\n";
        var keys = new Dictionary<string, string> { ["gc-max-time-slice"] = "3" };

        string result = UnityLaunchOptimizer.UpsertBootConfigKeys(content, keys);

        Assert.Contains("wait-for-native-debugger=0", result);
        Assert.Contains("hdr-display-enabled=0", result);
        Assert.Contains("gc-max-time-slice=3", result);
    }

    [Fact]
    public void UpsertBootConfigKeysIsIdempotentWhenValuesAlreadyCorrect()
    {
        string content = "gfx-enable-gfx-jobs=1\ngc-max-time-slice=3\n";
        var keys = new Dictionary<string, string>
        {
            ["gfx-enable-gfx-jobs"] = "1",
            ["gc-max-time-slice"] = "3",
        };

        string result = UnityLaunchOptimizer.UpsertBootConfigKeys(content, keys);

        Assert.Equal(content, result);
    }

    [Fact]
    public void UpsertBootConfigKeysHandlesContentWithNoPrecedingNewline()
    {
        const string content = "wait-for-native-debugger=0";
        var keys = new Dictionary<string, string> { ["gc-max-time-slice"] = "3" };

        string result = UnityLaunchOptimizer.UpsertBootConfigKeys(content, keys);

        Assert.Contains("wait-for-native-debugger=0", result);
        Assert.Contains("gc-max-time-slice=3", result);
    }

    // ── OptimizeAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task OptimizeAsyncPatchesBootConfigAndReturnsEnvVars()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "game_Data");
            Directory.CreateDirectory(dataDir);
            string bootConfig = Path.Combine(dataDir, "boot.config");
            await File.WriteAllTextAsync(bootConfig, "wait-for-native-debugger=0\n");
            string exe = Path.Combine(root, "game");

            UnityLaunchOptimizer sut = CreateSut(new UnityLaunchOptions
            {
                Enabled = true,
                BootConfig = new Dictionary<string, string> { ["gc-max-time-slice"] = "3" },
                EnvironmentVariables = new Dictionary<string, string> { ["mesa_glthread"] = "true" },
            });

            IReadOnlyDictionary<string, string> env = await sut.OptimizeAsync(exe, CancellationToken.None);

            string written = await File.ReadAllTextAsync(bootConfig);
            Assert.Contains("gc-max-time-slice=3", written);
            Assert.Contains("wait-for-native-debugger=0", written);
            Assert.Equal("true", env["mesa_glthread"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OptimizeAsyncReturnsEmptyAndSkipsIoWhenDisabled()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "game_Data");
            Directory.CreateDirectory(dataDir);
            string bootConfig = Path.Combine(dataDir, "boot.config");
            await File.WriteAllTextAsync(bootConfig, "wait-for-native-debugger=0\n");
            string exe = Path.Combine(root, "game");

            UnityLaunchOptimizer sut = CreateSut(new UnityLaunchOptions
            {
                Enabled = false,
                BootConfig = new Dictionary<string, string> { ["gc-max-time-slice"] = "3" },
                EnvironmentVariables = new Dictionary<string, string> { ["mesa_glthread"] = "true" },
            });

            IReadOnlyDictionary<string, string> env = await sut.OptimizeAsync(exe, CancellationToken.None);

            string written = await File.ReadAllTextAsync(bootConfig);
            Assert.DoesNotContain("gc-max-time-slice", written);
            Assert.Empty(env);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OptimizeAsyncReturnsEmptyWhenNotUnityGame()
    {
        string root = CreateTempDirectory();
        try
        {
            string exe = Path.Combine(root, "nongame");

            UnityLaunchOptimizer sut = CreateSut(new UnityLaunchOptions
            {
                Enabled = true,
                BootConfig = new Dictionary<string, string> { ["gc-max-time-slice"] = "3" },
                EnvironmentVariables = new Dictionary<string, string> { ["mesa_glthread"] = "true" },
            });

            IReadOnlyDictionary<string, string> env = await sut.OptimizeAsync(exe, CancellationToken.None);

            Assert.Empty(env);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static UnityLaunchOptimizer CreateSut(UnityLaunchOptions opts) =>
        new(Microsoft.Extensions.Options.Options.Create(opts), NullLogger<UnityLaunchOptimizer>.Instance);

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }
}
