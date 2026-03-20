using System.Net;
using System.Net.Http.Headers;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.IntegrationLite)]
public class IngestionSyncApiHostTests
{
    private static readonly Uri BaseUri = new("http://127.0.0.1:15123/");

    [Fact]
    public async Task ApiEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-auth-missing");
        await using var host = CreateHost(temp.RootPath, "token-123");
        await host.StartAsync();

        using var client = CreateHttpClient();

        using var response = await client.GetAsync("api/ingestions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiEndpoint_WithHeaderToken_ReturnsOk()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-auth-header");
        await using var host = CreateHost(temp.RootPath, "token-abc");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-abc");

        using var response = await client.GetAsync("api/ingestions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiEndpoint_WithBearerToken_ReturnsOk()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-auth-bearer");
        await using var host = CreateHost(temp.RootPath, "token-bearer");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-bearer");

        using var response = await client.GetAsync("api/ingestions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task VersionEndpoint_WithToken_ReturnsContractMetadata()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-version");
        await using var host = CreateHost(temp.RootPath, "token-version");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-version");

        using var response = await client.GetAsync("api/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal("ingestion-sync", json.RootElement.GetProperty("api").GetString());
        Assert.Equal("1.0.0", json.RootElement.GetProperty("version").GetString());
        Assert.True(json.RootElement.GetProperty("supports").GetProperty("ingestions").GetBoolean());
        Assert.True(json.RootElement.GetProperty("supports").GetProperty("events").GetBoolean());
        Assert.True(json.RootElement.GetProperty("supports").GetProperty("fromStateOverride").GetBoolean());
        Assert.True(json.RootElement.GetProperty("supports").GetProperty("metadata").GetBoolean());
        Assert.True(json.RootElement.GetProperty("supports").GetProperty("desktopState").GetBoolean());
        Assert.False(json.RootElement.GetProperty("runtime").GetProperty("allowSyncApiStateOverride").GetBoolean());
    }

    [Fact]
    public async Task VersionEndpoint_RuntimeOverrideFlagEnabled_ReflectsTrue()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-version-override");
        await using var host = CreateHost(temp.RootPath, "token-version-override", allowStateOverride: true);
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-version-override");

        using var response = await client.GetAsync("api/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("runtime").GetProperty("allowSyncApiStateOverride").GetBoolean());
    }

    [Fact]
    public async Task SingleIngestionEndpoint_ReturnsItem()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-single-ingestion");
        await using var host = CreateHost(temp.RootPath, "token-single-ingestion");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-single-ingestion");

        var ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-single", "https://drive.google.com/file/d/single/view");

        using var response = await client.GetAsync($"api/ingestions/{ingestionId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var item = json.RootElement.GetProperty("item");

        Assert.Equal(ingestionId, item.GetProperty("IngestionId").GetInt64());
        Assert.Equal("googledrive", item.GetProperty("Source").GetString());
        // Accept either Queued or Downloaded, depending on what POST /api/ingestions left it as
        Assert.Contains(item.GetProperty("CurrentState").GetString(), new[] { "Queued", "Downloaded" });
        Assert.Equal(LibraryIdentityService.BuildSourceKey("googledrive", "drive-id-single"), item.GetProperty("SourceId").GetString());
        Assert.Contains(item.GetProperty("DesktopState").GetString(), new[] { "Cloud", "Downloaded" });
    }

    [Fact]
    public async Task SingleIngestionEndpoint_NotFound_Returns404()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-single-ingestion-notfound");
        await using var host = CreateHost(temp.RootPath, "token-single-ingestion-notfound");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-single-ingestion-notfound");

        using var response = await client.GetAsync("api/ingestions/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateIngestion_WithMetadata_RoundTripsInSingleItemPayload()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-metadata");
        await using var host = CreateHost(temp.RootPath, "token-metadata");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-metadata");

        var createPayload = JsonSerializer.Serialize(new
        {
            source = "googledrive",
            sourceId = "drive-id-meta",
            sourceLink = "https://drive.google.com/file/d/meta/view",
            downloadedLocation = "/tmp/meta-song.zip",
            artist = "Tool",
            title = "Sober",
            charter = "Convour/clintilona/nunchuck/DenVaktare",
        });

        using var createResponse = await client.PostAsync(
            "api/ingestions",
            new StringContent(createPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        var createBody = await createResponse.Content.ReadAsStringAsync();
        using var createJson = JsonDocument.Parse(createBody);
        var ingestionId = createJson.RootElement.GetProperty("ingestionId").GetInt64();

        using var getResponse = await client.GetAsync($"api/ingestions/{ingestionId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var getBody = await getResponse.Content.ReadAsStringAsync();
        using var getJson = JsonDocument.Parse(getBody);
        var item = getJson.RootElement.GetProperty("item");

        Assert.Equal("Tool", item.GetProperty("Artist").GetString());
        Assert.Equal("Sober", item.GetProperty("Title").GetString());
        Assert.Equal("Convour/clintilona/nunchuck/DenVaktare", item.GetProperty("Charter").GetString());
    }

    [Fact]
    public async Task EventEndpoint_InvalidTransition_ReturnsBadRequest()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-invalid-transition");
        await using var host = CreateHost(temp.RootPath, "token-events");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-events");

        var createPayload = """
        {
          "source": "googledrive",
          "sourceId": "drive-id-1",
          "sourceLink": "https://drive.google.com/file/d/abc123/view",
          "downloadedLocation": "/tmp/song.zip"
        }
        """;

        using var createResponse = await client.PostAsync(
            "api/ingestions",
            new StringContent(createPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        var createBody = await createResponse.Content.ReadAsStringAsync();
        using var createJson = JsonDocument.Parse(createBody);
        var ingestionId = createJson.RootElement.GetProperty("ingestionId").GetInt64();

        var invalidEventPayload = """
        {
          "toState": "Installed",
          "details": "skip directly to installed"
        }
        """;

        using var eventResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(invalidEventPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, eventResponse.StatusCode);
    }

    [Fact]
    public async Task EventEndpoint_ResetPathTransition_IsAccepted()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-reset-transition");
        await using var host = CreateHost(temp.RootPath, "token-reset");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-reset");

        var ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-reset", "https://drive.google.com/file/d/reset1/view");

        var resetEventPayload = """
        {
          "toState": "ResolvingSource",
          "details": "resume processing"
        }
        """;

        using var response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(resetEventPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(ingestionId, json.RootElement.GetProperty("ingestionId").GetInt64());
        var fromState = json.RootElement.GetProperty("fromState").GetString();
        Assert.Contains(fromState, new[] { "Queued", "Downloaded" });
        Assert.Equal("ResolvingSource", json.RootElement.GetProperty("toState").GetString());
    }

    [Fact]
    public async Task EventEndpoint_CancelledToResolvingSource_ResetPath_IsAccepted()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-cancelled-reset");
        await using var host = CreateHost(temp.RootPath, "token-cancel-reset");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-cancel-reset");

        var ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-cancel", "https://drive.google.com/file/d/cancel1/view");

        var toCancelledPayload = """
        {
          "toState": "Cancelled",
          "details": "user paused"
        }
        """;

        using var toCancelledResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(toCancelledPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, toCancelledResponse.StatusCode);

        var fromCancelledPayload = """
        {
          "toState": "ResolvingSource",
          "details": "resume after pause"
        }
        """;

        using var fromCancelledResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(fromCancelledPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, fromCancelledResponse.StatusCode);

        var body = await fromCancelledResponse.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Cancelled", json.RootElement.GetProperty("fromState").GetString());
        Assert.Equal("ResolvingSource", json.RootElement.GetProperty("toState").GetString());
    }

    [Fact]
    public async Task EventEndpoint_FromStateMismatch_WithoutOverride_ReturnsConflict()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-fromstate-mismatch");
        await using var host = CreateHost(temp.RootPath, "token-mismatch");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-mismatch");

        var ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-mismatch", "https://drive.google.com/file/d/mismatch/view");

        var mismatchPayload = """
        {
          "fromState": "Cancelled",
          "toState": "ResolvingSource",
          "details": "intentional mismatch"
        }
        """;

        using var response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(mismatchPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task EventEndpoint_FromStateMismatch_WithOverride_IsAccepted()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-fromstate-override");
        await using var host = CreateHost(temp.RootPath, "token-override", allowStateOverride: true);
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-override");

        var ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-override", "https://drive.google.com/file/d/override/view");

        var overridePayload = """
        {
          "fromState": "Cancelled",
          "toState": "ResolvingSource",
          "details": "allow override",
          "allowFromStateOverride": true
        }
        """;

        using var response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(overridePayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Cancelled", json.RootElement.GetProperty("fromState").GetString());
        Assert.Equal("ResolvingSource", json.RootElement.GetProperty("toState").GetString());
    }

    [Fact]
    public async Task EventEndpoint_FromStateMismatch_WithOverrideFlagButRuntimeDisabled_ReturnsConflict()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-override-disabled");
        await using var host = CreateHost(temp.RootPath, "token-override-disabled", allowStateOverride: false);
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-override-disabled");

        var ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-override-disabled", "https://drive.google.com/file/d/override-disabled/view");

        var overridePayload = """
        {
          "fromState": "Cancelled",
          "toState": "ResolvingSource",
          "details": "runtime should still reject",
          "allowFromStateOverride": true
        }
        """;

        using var response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(overridePayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task EventEndpoint_NotFound_Returns404()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-notfound");
        await using var host = CreateHost(temp.RootPath, "token-notfound");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-notfound");

        var payload = """
        {
          "toState": "ResolvingSource",
          "details": "ingestion id does not exist"
        }
        """;

        using var response = await client.PostAsync(
            "api/ingestions/999999/events",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EventEndpoint_InvalidToState_ReturnsBadRequest()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-invalid-tostate");
        await using var host = CreateHost(temp.RootPath, "token-invalid-tostate");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-invalid-tostate");

        var ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-invalid-to", "https://drive.google.com/file/d/invalid-to/view");

        var payload = """
        {
          "toState": "NotARealState",
          "details": "invalid enum"
        }
        """;

        using var response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EventEndpoint_InvalidFromState_ReturnsBadRequest()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-invalid-fromstate");
        await using var host = CreateHost(temp.RootPath, "token-invalid-fromstate");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-invalid-fromstate");

        var ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-invalid-from", "https://drive.google.com/file/d/invalid-from/view");

        var payload = """
        {
          "fromState": "Nope",
          "toState": "ResolvingSource",
          "details": "invalid from enum"
        }
        """;

        using var response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ActionEndpoint_Retry_TransitionsToQueued()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-action-retry");
        await using var host = CreateHost(temp.RootPath, "token-action-retry");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-action-retry");

        var ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-action-retry", "https://drive.google.com/file/d/action-retry/view");

        using var setCancelledResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent("{\"toState\":\"Cancelled\",\"details\":\"pause\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, setCancelledResponse.StatusCode);

        using var actionResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/retry",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, actionResponse.StatusCode);

        var actionBody = await actionResponse.Content.ReadAsStringAsync();
        using var actionJson = JsonDocument.Parse(actionBody);
        Assert.Equal(ingestionId, actionJson.RootElement.GetProperty("ingestionId").GetInt64());
        Assert.Equal("retry", actionJson.RootElement.GetProperty("action").GetString());
        Assert.Equal("Cancelled", actionJson.RootElement.GetProperty("fromState").GetString());
        Assert.Equal("Queued", actionJson.RootElement.GetProperty("toState").GetString());
    }

    [Fact]
    public async Task ActionEndpoint_Install_InstallsZipAndReturnsOutputDirectories()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-action-install");
        var zipPath = CreateTestZip(temp.RootPath, "action-install-song.zip");

        await using var host = CreateHost(temp.RootPath, "token-action-install");
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-action-install");

        var ingestionId = await CreateDownloadedIngestionAsync(
            client,
            "drive-id-action-install",
            "https://drive.google.com/file/d/action-install/view",
            zipPath);

        using var response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/install",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var installedDirs = json.RootElement.GetProperty("installedDirectories").EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        Assert.NotEmpty(installedDirs);
        Assert.All(installedDirs, dir => Assert.True(Directory.Exists(dir!)));
    }

    [Fact]
    public async Task ActionEndpoint_OpenFolder_UsesInstalledDirectoryWhenAvailable()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-action-open-installed");
        var opener = new FakeDesktopPathOpener();
        var zipPath = CreateTestZip(temp.RootPath, "open-folder-installed.zip");

        await using var host = CreateHost(temp.RootPath, "token-open-installed", opener: opener);
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-open-installed");

        var ingestionId = await CreateDownloadedIngestionAsync(
            client,
            "drive-id-open-installed",
            "https://drive.google.com/file/d/open-installed/view",
            zipPath);

        using var installResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/install",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, installResponse.StatusCode);

        using var openResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/open-folder",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, openResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(opener.LastOpenedDirectory));
        Assert.True(Directory.Exists(opener.LastOpenedDirectory!));
    }

    [Fact]
    public async Task ActionEndpoint_OpenFolder_FallsBackToDownloadedParentDirectory()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-action-open-download-parent");
        var opener = new FakeDesktopPathOpener();
        var downloadDir = Path.Combine(temp.RootPath, "Downloads");
        Directory.CreateDirectory(downloadDir);
        var zipPath = CreateTestZip(downloadDir, "open-folder-download.zip");

        await using var host = CreateHost(temp.RootPath, "token-open-download-parent", opener: opener);
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-open-download-parent");

        var ingestionId = await CreateDownloadedIngestionAsync(
            client,
            "drive-id-open-download-parent",
            "https://drive.google.com/file/d/open-download-parent/view",
            zipPath);

        using var openResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/open-folder",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, openResponse.StatusCode);
        Assert.Equal(downloadDir, opener.LastOpenedDirectory);
    }

    [Fact]
    public async Task ActionEndpoint_OpenFolder_WithoutUsableDirectory_ReturnsConflict()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-action-open-missing");
        var opener = new FakeDesktopPathOpener();

        await using var host = CreateHost(temp.RootPath, "token-open-missing", opener: opener);
        await host.StartAsync();

        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-open-missing");

        var missingPath = Path.Combine(temp.RootPath, "missing", "song.zip");
        var ingestionId = await CreateDownloadedIngestionAsync(
            client,
            "drive-id-open-missing",
            "https://drive.google.com/file/d/open-missing/view",
            missingPath);

        using var openResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/open-folder",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, openResponse.StatusCode);
        Assert.Null(opener.LastOpenedDirectory);
    }

    private static async Task<long> CreateDownloadedIngestionAsync(HttpClient client, string sourceId, string sourceLink, string downloadedLocation = "/tmp/song.zip")
    {
        var createPayload = JsonSerializer.Serialize(new
        {
            source = "googledrive",
            sourceId,
            sourceLink,
            downloadedLocation,
        });

        using var createResponse = await client.PostAsync(
            "api/ingestions",
            new StringContent(createPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        var createBody = await createResponse.Content.ReadAsStringAsync();
        using var createJson = JsonDocument.Parse(createBody);
        return createJson.RootElement.GetProperty("ingestionId").GetInt64();
    }

    private static string CreateTestZip(string rootPath, string fileName)
    {
        var zipPath = Path.Combine(rootPath, fileName);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("song/chart.mid");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("test-midi-data");
        return zipPath;
    }

    private static IngestionSyncApiHost CreateHost(
        string rootPath,
        string token,
        bool allowStateOverride = false,
        IDesktopPathOpener? opener = null)
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
                SyncApiAuthToken = token,
                AllowSyncApiStateOverride = allowStateOverride,
            },
        };

        foreach (var dir in new[]
        {
            config.Runtime.TempDirectory,
            config.Runtime.DownloadDirectory,
            config.Runtime.StagingDirectory,
            config.Runtime.OutputDirectory,
            config.Runtime.CloneHeroDataDirectory,
            config.Runtime.CloneHeroSongDirectory,
        })
        {
            Directory.CreateDirectory(dir);
        }

        var settings = new AppGlobalSettings(new FakeSettingsOrchestrator(config));
        var catalog = new SongIngestionCatalogService(Path.Combine(rootPath, "library-catalog.db"));
        var stateMachine = new SongIngestionStateMachine();

        var installer = new SongInstallService(settings, catalog, stateMachine, new OnyxService(settings));
        return new IngestionSyncApiHost(catalog, stateMachine, settings, installer, opener ?? new FakeDesktopPathOpener());
    }

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            BaseAddress = BaseUri,
            Timeout = TimeSpan.FromSeconds(5),
        };
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

    private sealed class FakeDesktopPathOpener : IDesktopPathOpener
    {
        public string? LastOpenedDirectory { get; private set; }

        public Task OpenDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastOpenedDirectory = directoryPath;
            return Task.CompletedTask;
        }
    }
}
