using System.Text.Json;

using ChartHub.Configuration.Models;
using ChartHub.Configuration.Stores;
using ChartHub.Tests.TestInfrastructure;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.IntegrationLite)]
public class JsonAppConfigStoreTests
{
    [Fact]
    public void Load_WhenConfigMissing_CreatesDefaultConfigFile()
    {
        using var temp = new TemporaryDirectoryFixture("json-config-missing");
        string configPath = temp.GetPath("appsettings.json");

        using var sut = new JsonAppConfigStore(configPath);
        AppConfigRoot config = sut.Load();

        Assert.True(File.Exists(configPath));
        Assert.Equal(2, config.ConfigVersion);
        Assert.NotNull(config.Runtime);
        Assert.NotNull(config.GoogleAuth);
    }

    [Fact]
    public void Load_WhenLegacyFlatKeysPresent_MapsIntoRuntimeAndGoogleAuth()
    {
        using var temp = new TemporaryDirectoryFixture("json-config-legacy");
        string configPath = temp.GetPath("appsettings.json");

        string legacy = """
        {
          "ConfigVersion": 0,
          "UseMockData": true,
          "TempDirectory": "/tmp/legacy-temp",
          "DownloadDirectory": "/tmp/legacy-downloads",
                    "SyncApiAuthToken": "legacy-sync-token",
          "GoogleDrive": {
            "android_client_id": "android-id",
            "desktop_client_id": "desktop-id"
          }
        }
        """;
        File.WriteAllText(configPath, legacy);

        using var sut = new JsonAppConfigStore(configPath);
        AppConfigRoot config = sut.Load();

        Assert.Equal(2, config.ConfigVersion);
        Assert.True(config.Runtime.UseMockData);
        Assert.Equal("legacy-sync-token", config.Runtime.ServerApiAuthToken);
        Assert.Equal("android-id", config.GoogleAuth.AndroidClientId);
        Assert.Equal("desktop-id", config.GoogleAuth.DesktopClientId);

        string migrated = File.ReadAllText(configPath);
        Assert.DoesNotContain("TempDirectory", migrated, StringComparison.Ordinal);
        Assert.DoesNotContain("DownloadDirectory", migrated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsync_WritesConfigAndCleansTemporaryFile()
    {
        using var temp = new TemporaryDirectoryFixture("json-config-save");
        string configPath = temp.GetPath("appsettings.json");

        using var sut = new JsonAppConfigStore(configPath);
        AppConfigRoot config = sut.Load();
        config.Runtime.ServerApiBaseUrl = "https://server.example";

        await sut.SaveAsync(config);

        Assert.True(File.Exists(configPath));
        Assert.False(File.Exists(configPath + ".tmp"));

        string text = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(text);
        string? output = doc.RootElement
            .GetProperty("Runtime")
            .GetProperty("ServerApiBaseUrl")
            .GetString();
        Assert.Equal("https://server.example", output);
    }
}
