using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
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
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-123");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();

        using HttpResponseMessage response = await client.GetAsync("api/ingestions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiEndpoint_WithHeaderToken_ReturnsOk()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-auth-header");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-abc");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-abc");

        using HttpResponseMessage response = await client.GetAsync("api/ingestions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiEndpoint_WithBearerToken_ReturnsOk()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-auth-bearer");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-bearer");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-bearer");

        using HttpResponseMessage response = await client.GetAsync("api/ingestions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PairClaimEndpoint_WithValidCode_ReturnsTokenWithoutAuthHeader()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-pair-claim-ok");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-pair-ok", pairCode: "PAIR-1234");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        string payload = JsonSerializer.Serialize(new
        {
            pairCode = "PAIR-1234",
            deviceLabel = "Pixel Companion",
        });

        using HttpResponseMessage response = await client.PostAsync(
            "api/pair/claim",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("paired").GetBoolean());
        Assert.Equal("token-pair-ok", json.RootElement.GetProperty("token").GetString());
    }

    [Fact]
    public async Task PairClaimEndpoint_WithConfiguredListenPrefix_ReturnsConfiguredApiBaseUrl()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-pair-claim-listen-prefix");
        await using IngestionSyncApiHost host = CreateHost(
            temp.RootPath,
            "token-pair-prefix",
            pairCode: "PAIR-1234",
            listenPrefix: "http://localhost:15123/");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient(new Uri("http://localhost:15123/"));
        string payload = JsonSerializer.Serialize(new
        {
            pairCode = "PAIR-1234",
            deviceLabel = "Pixel Companion",
        });

        using HttpResponseMessage response = await client.PostAsync(
            "api/pair/claim",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("http://localhost:15123", json.RootElement.GetProperty("apiBaseUrl").GetString());
    }

    [Fact]
    public async Task PairClaimEndpoint_WithAdvertisedBaseUrlOverride_ReturnsOverride()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-pair-claim-advertised-override");
        await using IngestionSyncApiHost host = CreateHost(
            temp.RootPath,
            "token-pair-override",
            pairCode: "PAIR-1234",
            listenPrefix: "http://localhost:15123/",
            advertisedBaseUrl: "http://192.168.1.55:15123");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient(new Uri("http://localhost:15123/"));
        string payload = JsonSerializer.Serialize(new
        {
            pairCode = "PAIR-1234",
            deviceLabel = "Pixel Companion",
        });

        using HttpResponseMessage response = await client.PostAsync(
            "api/pair/claim",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("http://192.168.1.55:15123", json.RootElement.GetProperty("apiBaseUrl").GetString());
    }

    [Fact]
    public async Task PairClaimEndpoint_WithInvalidCode_ReturnsUnauthorized()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-pair-claim-bad-code");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-pair-bad", pairCode: "PAIR-1234");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        string payload = JsonSerializer.Serialize(new
        {
            pairCode = "WRONG-CODE",
            deviceLabel = "Pixel Companion",
        });

        using HttpResponseMessage response = await client.PostAsync(
            "api/pair/claim",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PairClaimEndpoint_WithoutConfiguredCode_ReturnsUnauthorized()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-pair-claim-missing");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-pair-missing", pairCode: string.Empty);
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        string payload = JsonSerializer.Serialize(new
        {
            pairCode = "PAIR-1234",
            deviceLabel = "Pixel Companion",
        });

        using HttpResponseMessage response = await client.PostAsync(
            "api/pair/claim",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PairClaimEndpoint_WithExpiredCode_ReturnsGone()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-pair-claim-expired");
        await using IngestionSyncApiHost host = CreateHost(
            temp.RootPath,
            "token-pair-expired",
            pairCode: "PAIR-1234",
            pairCodeIssuedAtUtc: DateTimeOffset.UtcNow.AddHours(-2).ToString("O"),
            pairCodeTtlMinutes: 10);
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        string payload = JsonSerializer.Serialize(new
        {
            pairCode = "PAIR-1234",
            deviceLabel = "Pixel Companion",
        });

        using HttpResponseMessage response = await client.PostAsync(
            "api/pair/claim",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task PairClaimEndpoint_IsOneTimeUse_SecondClaimFails()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-pair-claim-onetime");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-pair-onetime", pairCode: "PAIR-1234");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        string payload = JsonSerializer.Serialize(new
        {
            pairCode = "PAIR-1234",
            deviceLabel = "Pixel Companion",
        });

        using HttpResponseMessage firstResponse = await client.PostAsync(
            "api/pair/claim",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using HttpResponseMessage secondResponse = await client.PostAsync(
            "api/pair/claim",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, secondResponse.StatusCode);
    }

    [Fact]
    public async Task PairClaimEndpoint_AppendsAndTrimsPairingHistory()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-pair-claim-history");
        (IngestionSyncApiHost? host, AppGlobalSettings? settings) = CreateHostWithSettings(temp.RootPath, "token-pair-history", pairCode: "PAIR-1234");
        await using (host)
        {
            await host.StartAsync();

            using HttpClient client = CreateHttpClient();
            for (int index = 1; index <= 12; index++)
            {
                string payload = JsonSerializer.Serialize(new
                {
                    pairCode = settings.SyncApiPairCode,
                    deviceLabel = $"Companion {index}",
                });

                using HttpResponseMessage response = await client.PostAsync(
                    "api/pair/claim",
                    new StringContent(payload, Encoding.UTF8, "application/json"));

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        Assert.Equal("Companion 12", settings.SyncApiLastPairedDeviceLabel);
        Assert.False(string.IsNullOrWhiteSpace(settings.SyncApiLastPairedAtUtc));

        using var historyDocument = JsonDocument.Parse(settings.SyncApiPairingHistoryJson);
        JsonElement[] history = historyDocument.RootElement.EnumerateArray().ToArray();
        Assert.Equal(10, history.Length);
        Assert.Equal("Companion 12", history[0].GetProperty("deviceLabel").GetString());
        string? tail = history[9].GetProperty("deviceLabel").GetString();
        Assert.Contains(tail, new[] { "Companion 2", "Companion 3" });
    }

    [Fact]
    public async Task VersionEndpoint_WithToken_ReturnsContractMetadata()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-version");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-version");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-version");

        using HttpResponseMessage response = await client.GetAsync("api/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal("ingestion-sync", json.RootElement.GetProperty("api").GetString());
        Assert.Equal("1.0.0", json.RootElement.GetProperty("version").GetString());
        Assert.True(json.RootElement.GetProperty("supports").GetProperty("ingestions").GetBoolean());
        Assert.True(json.RootElement.GetProperty("supports").GetProperty("events").GetBoolean());
        Assert.True(json.RootElement.GetProperty("supports").GetProperty("fromStateOverride").GetBoolean());
        Assert.True(json.RootElement.GetProperty("supports").GetProperty("metadata").GetBoolean());
        Assert.True(json.RootElement.GetProperty("supports").GetProperty("desktopState").GetBoolean());
        JsonElement runtime = json.RootElement.GetProperty("runtime");
        Assert.False(runtime.GetProperty("allowSyncApiStateOverride").GetBoolean());
        Assert.Equal(65536, runtime.GetProperty("maxRequestBodyBytes").GetInt32());
        Assert.Equal(1000, runtime.GetProperty("bodyReadTimeoutMs").GetInt32());
        Assert.Equal(250, runtime.GetProperty("mutationWaitTimeoutMs").GetInt32());
        Assert.Equal(500, runtime.GetProperty("slowRequestThresholdMs").GetInt32());

        JsonElement telemetry = runtime.GetProperty("telemetry");
        Assert.True(telemetry.GetProperty("requestsTotal").GetInt64() >= 0);
        Assert.True(telemetry.GetProperty("slowRequestsTotal").GetInt64() >= 0);
        Assert.True(telemetry.GetProperty("busyMutationRejectionsTotal").GetInt64() >= 0);
        Assert.True(telemetry.GetProperty("clientErrorsTotal").GetInt64() >= 0);
        Assert.True(telemetry.GetProperty("serverErrorsTotal").GetInt64() >= 0);
        Assert.NotNull(telemetry.GetProperty("startedAtUtc").GetString());
    }

    [Fact]
    public async Task VersionEndpoint_RuntimeOverrideFlagEnabled_ReflectsTrue()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-version-override");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-version-override", allowStateOverride: true);
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-version-override");

        using HttpResponseMessage response = await client.GetAsync("api/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("runtime").GetProperty("allowSyncApiStateOverride").GetBoolean());
    }

    [Fact]
    public async Task VersionEndpoint_CustomThresholds_ReflectConfiguredValues()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-version-thresholds");
        await using IngestionSyncApiHost host = CreateHost(
            temp.RootPath,
            "token-version-thresholds",
            syncApiMaxRequestBodyBytes: 32768,
            syncApiBodyReadTimeoutMs: 1500,
            syncApiMutationWaitTimeoutMs: 600,
            syncApiSlowRequestThresholdMs: 1200);
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-version-thresholds");

        using HttpResponseMessage response = await client.GetAsync("api/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        JsonElement runtime = json.RootElement.GetProperty("runtime");
        Assert.Equal(32768, runtime.GetProperty("maxRequestBodyBytes").GetInt32());
        Assert.Equal(1500, runtime.GetProperty("bodyReadTimeoutMs").GetInt32());
        Assert.Equal(600, runtime.GetProperty("mutationWaitTimeoutMs").GetInt32());
        Assert.Equal(1200, runtime.GetProperty("slowRequestThresholdMs").GetInt32());
        Assert.True(runtime.GetProperty("telemetry").GetProperty("requestsTotal").GetInt64() >= 0);
    }

    [Fact]
    public async Task IngestionsEndpoint_WithLimitParameter_ReturnsClampedSubset()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-list-limit");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-list-limit");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-list-limit");

        await CreateDownloadedIngestionAsync(client, "drive-id-limit-1", "https://drive.google.com/file/d/limit-1/view");
        await CreateDownloadedIngestionAsync(client, "drive-id-limit-2", "https://drive.google.com/file/d/limit-2/view");

        using HttpResponseMessage response = await client.GetAsync("api/ingestions?limit=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task SingleIngestionEndpoint_ReturnsItem()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-single-ingestion");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-single-ingestion");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-single-ingestion");

        long ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-single", "https://drive.google.com/file/d/single/view");

        using HttpResponseMessage response = await client.GetAsync($"api/ingestions/{ingestionId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        JsonElement item = json.RootElement.GetProperty("item");

        Assert.Equal(ingestionId, item.GetProperty("IngestionId").GetInt64());
        Assert.Equal("rhythmverse", item.GetProperty("Source").GetString());
        // Accept either Queued or Downloaded, depending on what POST /api/ingestions left it as
        Assert.Contains(item.GetProperty("CurrentState").GetString(), new[] { "Queued", "Downloaded" });
        Assert.Equal(LibraryIdentityService.BuildSourceKey("rhythmverse", "drive-id-single"), item.GetProperty("SourceId").GetString());
        Assert.Contains(item.GetProperty("DesktopState").GetString(), new[] { "Cloud", "Downloaded" });
    }

    [Fact]
    public async Task SingleIngestionEndpoint_NotFound_Returns404()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-single-ingestion-notfound");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-single-ingestion-notfound");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-single-ingestion-notfound");

        using HttpResponseMessage response = await client.GetAsync("api/ingestions/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateIngestion_WithMetadata_RoundTripsInSingleItemPayload()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-metadata");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-metadata");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-metadata");

        string createPayload = JsonSerializer.Serialize(new
        {
            source = "rhythmverse",
            sourceId = "drive-id-meta",
            sourceLink = "https://drive.google.com/file/d/meta/view",
            downloadedLocation = "/tmp/meta-song.zip",
            artist = "Tool",
            title = "Sober",
            charter = "Convour/clintilona/nunchuck/DenVaktare",
            librarySource = "rhythmverse",
        });

        using HttpResponseMessage createResponse = await client.PostAsync(
            "api/ingestions",
            new StringContent(createPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        string createBody = await createResponse.Content.ReadAsStringAsync();
        using var createJson = JsonDocument.Parse(createBody);
        long ingestionId = createJson.RootElement.GetProperty("ingestionId").GetInt64();

        using HttpResponseMessage getResponse = await client.GetAsync($"api/ingestions/{ingestionId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        string getBody = await getResponse.Content.ReadAsStringAsync();
        using var getJson = JsonDocument.Parse(getBody);
        JsonElement item = getJson.RootElement.GetProperty("item");

        Assert.Equal("Tool", item.GetProperty("Artist").GetString());
        Assert.Equal("Sober", item.GetProperty("Title").GetString());
        Assert.Equal("Convour/clintilona/nunchuck/DenVaktare", item.GetProperty("Charter").GetString());
        Assert.Equal("rhythmverse", item.GetProperty("LibrarySource").GetString());
    }

    [Fact]
    public async Task CreateIngestion_WithOversizedBody_ReturnsRequestEntityTooLarge()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-create-oversized");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-create-oversized");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-create-oversized");

        string payload = JsonSerializer.Serialize(new
        {
            source = "rhythmverse",
            sourceId = "drive-id-large",
            sourceLink = "https://drive.google.com/file/d/large/view",
            title = new string('a', 70_000),
        });

        using HttpResponseMessage response = await client.PostAsync(
            "api/ingestions",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task CreateIngestion_WithUnsupportedMediaType_ReturnsUnsupportedMediaType()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-create-text");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-create-text");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-create-text");

        using HttpResponseMessage response = await client.PostAsync(
            "api/ingestions",
            new StringContent("source=rhythmverse", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task CreateIngestion_WithUnsupportedSource_ReturnsBadRequest()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-create-invalid-source");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-create-invalid-source");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-create-invalid-source");

        string payload = JsonSerializer.Serialize(new
        {
            source = "local",
            sourceId = "local-id",
            sourceLink = "file:///tmp/song.zip",
        });

        using HttpResponseMessage response = await client.PostAsync(
            "api/ingestions",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("source must be rhythmverse or encore", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateIngestion_WithUnsupportedLibrarySource_ReturnsBadRequest()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-create-invalid-library-source");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-create-invalid-library-source");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-create-invalid-library-source");

        string payload = JsonSerializer.Serialize(new
        {
            source = "rhythmverse",
            sourceId = "rv-id",
            sourceLink = "https://example.test/song.zip",
            librarySource = "import",
        });

        using HttpResponseMessage response = await client.PostAsync(
            "api/ingestions",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("librarySource must be rhythmverse or encore", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EventEndpoint_InvalidTransition_ReturnsBadRequest()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-invalid-transition");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-events");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-events");

        string createPayload = """
        {
          "source": "rhythmverse",
          "sourceId": "drive-id-1",
          "sourceLink": "https://drive.google.com/file/d/abc123/view",
          "downloadedLocation": "/tmp/song.zip"
        }
        """;

        using HttpResponseMessage createResponse = await client.PostAsync(
            "api/ingestions",
            new StringContent(createPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        string createBody = await createResponse.Content.ReadAsStringAsync();
        using var createJson = JsonDocument.Parse(createBody);
        long ingestionId = createJson.RootElement.GetProperty("ingestionId").GetInt64();

        string invalidEventPayload = """
        {
          "toState": "Installed",
          "details": "skip directly to installed"
        }
        """;

        using HttpResponseMessage eventResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(invalidEventPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, eventResponse.StatusCode);
    }

    [Fact]
    public async Task EventEndpoint_ResetPathTransition_IsAccepted()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-reset-transition");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-reset");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-reset");

        long ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-reset", "https://drive.google.com/file/d/reset1/view");

        string resetEventPayload = """
        {
          "toState": "ResolvingSource",
          "details": "resume processing"
        }
        """;

        using HttpResponseMessage response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(resetEventPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(ingestionId, json.RootElement.GetProperty("ingestionId").GetInt64());
        string? fromState = json.RootElement.GetProperty("fromState").GetString();
        Assert.Contains(fromState, new[] { "Queued", "Downloaded" });
        Assert.Equal("ResolvingSource", json.RootElement.GetProperty("toState").GetString());
    }

    [Fact]
    public async Task EventEndpoint_CancelledToResolvingSource_ResetPath_IsAccepted()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-cancelled-reset");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-cancel-reset");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-cancel-reset");

        long ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-cancel", "https://drive.google.com/file/d/cancel1/view");

        string toCancelledPayload = """
        {
          "toState": "Cancelled",
          "details": "user paused"
        }
        """;

        using HttpResponseMessage toCancelledResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(toCancelledPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, toCancelledResponse.StatusCode);

        string fromCancelledPayload = """
        {
          "toState": "ResolvingSource",
          "details": "resume after pause"
        }
        """;

        using HttpResponseMessage fromCancelledResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(fromCancelledPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, fromCancelledResponse.StatusCode);

        string body = await fromCancelledResponse.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Cancelled", json.RootElement.GetProperty("fromState").GetString());
        Assert.Equal("ResolvingSource", json.RootElement.GetProperty("toState").GetString());
    }

    [Fact]
    public async Task EventEndpoint_FromStateMismatch_WithoutOverride_ReturnsConflict()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-fromstate-mismatch");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-mismatch");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-mismatch");

        long ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-mismatch", "https://drive.google.com/file/d/mismatch/view");

        string mismatchPayload = """
        {
          "fromState": "Cancelled",
          "toState": "ResolvingSource",
          "details": "intentional mismatch"
        }
        """;

        using HttpResponseMessage response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(mismatchPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task EventEndpoint_FromStateMismatch_WithOverride_IsAccepted()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-fromstate-override");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-override", allowStateOverride: true);
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-override");

        long ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-override", "https://drive.google.com/file/d/override/view");

        string overridePayload = """
        {
          "fromState": "Cancelled",
          "toState": "ResolvingSource",
          "details": "allow override",
          "allowFromStateOverride": true
        }
        """;

        using HttpResponseMessage response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(overridePayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Cancelled", json.RootElement.GetProperty("fromState").GetString());
        Assert.Equal("ResolvingSource", json.RootElement.GetProperty("toState").GetString());
    }

    [Fact]
    public async Task EventEndpoint_FromStateMismatch_WithOverrideFlagButRuntimeDisabled_ReturnsConflict()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-override-disabled");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-override-disabled", allowStateOverride: false);
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-override-disabled");

        long ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-override-disabled", "https://drive.google.com/file/d/override-disabled/view");

        string overridePayload = """
        {
          "fromState": "Cancelled",
          "toState": "ResolvingSource",
          "details": "runtime should still reject",
          "allowFromStateOverride": true
        }
        """;

        using HttpResponseMessage response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(overridePayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task EventEndpoint_ConcurrentRequests_WithSameFromState_OneConflicts()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-events-concurrent");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-events-concurrent");
        await host.StartAsync();

        using HttpClient clientA = CreateHttpClient();
        clientA.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-events-concurrent");
        using HttpClient clientB = CreateHttpClient();
        clientB.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-events-concurrent");

        long ingestionId = await CreateDownloadedIngestionAsync(clientA, "drive-id-concurrent", "https://drive.google.com/file/d/concurrent/view");

        using HttpResponseMessage seedResponse = await clientA.PostAsync(
                $"api/ingestions/{ingestionId}/events",
                new StringContent("{\"toState\":\"Cancelled\",\"details\":\"seed-cancelled\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, seedResponse.StatusCode);

        string payloadA = """
        {
                    "fromState": "Cancelled",
                    "toState": "ResolvingSource",
          "details": "event-a"
        }
        """;

        string payloadB = """
        {
                    "fromState": "Cancelled",
          "toState": "ResolvingSource",
          "details": "event-b"
        }
        """;

        Task<HttpResponseMessage> requestA = clientA.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(payloadA, Encoding.UTF8, "application/json"));
        Task<HttpResponseMessage> requestB = clientB.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(payloadB, Encoding.UTF8, "application/json"));

        await Task.WhenAll(requestA, requestB);

        using HttpResponseMessage responseA = await requestA;
        using HttpResponseMessage responseB = await requestB;
        HttpStatusCode[] statuses = new[] { responseA.StatusCode, responseB.StatusCode };
        Assert.Contains(HttpStatusCode.Accepted, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);
    }

    [Fact]
    public async Task EventEndpoint_NotFound_Returns404()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-notfound");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-notfound");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-notfound");

        string payload = """
        {
          "toState": "ResolvingSource",
          "details": "ingestion id does not exist"
        }
        """;

        using HttpResponseMessage response = await client.PostAsync(
            "api/ingestions/999999/events",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EventEndpoint_InvalidToState_ReturnsBadRequest()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-invalid-tostate");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-invalid-tostate");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-invalid-tostate");

        long ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-invalid-to", "https://drive.google.com/file/d/invalid-to/view");

        string payload = """
        {
          "toState": "NotARealState",
          "details": "invalid enum"
        }
        """;

        using HttpResponseMessage response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EventEndpoint_InvalidFromState_ReturnsBadRequest()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-invalid-fromstate");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-invalid-fromstate");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-invalid-fromstate");

        long ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-invalid-from", "https://drive.google.com/file/d/invalid-from/view");

        string payload = """
        {
          "fromState": "Nope",
          "toState": "ResolvingSource",
          "details": "invalid from enum"
        }
        """;

        using HttpResponseMessage response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EventEndpoint_WithOversizedBody_ReturnsRequestEntityTooLarge()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-event-oversized");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-event-oversized");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-event-oversized");

        long ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-event-large", "https://drive.google.com/file/d/event-large/view");
        string payload = JsonSerializer.Serialize(new
        {
            toState = "ResolvingSource",
            details = new string('b', 70_000),
        });

        using HttpResponseMessage response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task ActionEndpoint_Retry_TransitionsToQueued()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-action-retry");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-action-retry");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-action-retry");

        long ingestionId = await CreateDownloadedIngestionAsync(client, "drive-id-action-retry", "https://drive.google.com/file/d/action-retry/view");

        using HttpResponseMessage setCancelledResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/events",
            new StringContent("{\"toState\":\"Cancelled\",\"details\":\"pause\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, setCancelledResponse.StatusCode);

        using HttpResponseMessage actionResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/retry",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, actionResponse.StatusCode);

        string actionBody = await actionResponse.Content.ReadAsStringAsync();
        using var actionJson = JsonDocument.Parse(actionBody);
        Assert.Equal(ingestionId, actionJson.RootElement.GetProperty("ingestionId").GetInt64());
        Assert.Equal("retry", actionJson.RootElement.GetProperty("action").GetString());
        Assert.Equal("Cancelled", actionJson.RootElement.GetProperty("fromState").GetString());
        Assert.Equal("Queued", actionJson.RootElement.GetProperty("toState").GetString());
        string? correlationId = actionJson.RootElement.GetProperty("correlationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Fact]
    public async Task ActionEndpoint_Install_InstallsZipAndReturnsOutputDirectories()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-action-install");
        string downloadDir = Path.Combine(temp.RootPath, "Downloads");
        string zipPath = CreateTestZip(downloadDir, "action-install-song.zip");

        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-action-install");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-action-install");

        long ingestionId = await CreateDownloadedIngestionAsync(
            client,
            "drive-id-action-install",
            "https://drive.google.com/file/d/action-install/view",
            zipPath);

        using HttpResponseMessage response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/install",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var installedDirs = json.RootElement.GetProperty("installedDirectories").EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        Assert.NotEmpty(installedDirs);
        Assert.All(installedDirs, dir => Assert.True(Directory.Exists(dir!)));
        string? correlationId = json.RootElement.GetProperty("correlationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Fact]
    public async Task ActionEndpoint_OpenFolder_UsesInstalledDirectoryWhenAvailable()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-action-open-installed");
        var opener = new FakeDesktopPathOpener();
        string downloadDir = Path.Combine(temp.RootPath, "Downloads");
        string zipPath = CreateTestZip(downloadDir, "open-folder-installed.zip");

        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-open-installed", opener: opener);
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-open-installed");

        long ingestionId = await CreateDownloadedIngestionAsync(
            client,
            "drive-id-open-installed",
            "https://drive.google.com/file/d/open-installed/view",
            zipPath);

        using HttpResponseMessage installResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/install",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, installResponse.StatusCode);

        using HttpResponseMessage openResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/open-folder",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, openResponse.StatusCode);
        string openBody = await openResponse.Content.ReadAsStringAsync();
        using var openJson = JsonDocument.Parse(openBody);
        string? correlationId = openJson.RootElement.GetProperty("correlationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
        Assert.True(Guid.TryParse(correlationId, out _));
        Assert.False(string.IsNullOrWhiteSpace(opener.LastOpenedDirectory));
        Assert.True(Directory.Exists(opener.LastOpenedDirectory!));
    }

    [Fact]
    public async Task ActionEndpoint_OpenFolder_FallsBackToDownloadedParentDirectory()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-action-open-download-parent");
        var opener = new FakeDesktopPathOpener();
        string downloadDir = Path.Combine(temp.RootPath, "Downloads");
        Directory.CreateDirectory(downloadDir);
        string zipPath = CreateTestZip(downloadDir, "open-folder-download.zip");

        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-open-download-parent", opener: opener);
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-open-download-parent");

        long ingestionId = await CreateDownloadedIngestionAsync(
            client,
            "drive-id-open-download-parent",
            "https://drive.google.com/file/d/open-download-parent/view",
            zipPath);

        using HttpResponseMessage openResponse = await client.PostAsync(
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

        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-open-missing", opener: opener);
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-open-missing");

        string missingPath = Path.Combine(temp.RootPath, "missing", "song.zip");
        long ingestionId = await CreateDownloadedIngestionAsync(
            client,
            "drive-id-open-missing",
            "https://drive.google.com/file/d/open-missing/view",
            missingPath);

        using HttpResponseMessage openResponse = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/open-folder",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, openResponse.StatusCode);
        Assert.Null(opener.LastOpenedDirectory);
    }

    [Fact]
    public async Task ActionEndpoint_UnknownAction_ReturnsNotFound()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-action-unknown");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-action-unknown");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-action-unknown");

        long ingestionId = await CreateDownloadedIngestionAsync(
            client,
            "drive-id-action-unknown",
            "https://drive.google.com/file/d/action-unknown/view");

        using HttpResponseMessage response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/not-a-real-action",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ActionEndpoint_Install_WithPathOutsideManagedRoots_ReturnsConflict()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-action-install-unmanaged");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-action-install-unmanaged");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-action-install-unmanaged");

        long ingestionId = await CreateDownloadedIngestionAsync(
            client,
            "drive-id-action-install-unmanaged",
            "https://drive.google.com/file/d/action-install-unmanaged/view",
            "/etc/passwd");

        using HttpResponseMessage response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/install",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ActionEndpoint_OpenFolder_WithPathOutsideManagedRoots_ReturnsConflict()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-action-open-unmanaged");
        var opener = new FakeDesktopPathOpener();

        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-action-open-unmanaged", opener: opener);
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-action-open-unmanaged");

        long ingestionId = await CreateDownloadedIngestionAsync(
            client,
            "drive-id-action-open-unmanaged",
            "https://drive.google.com/file/d/action-open-unmanaged/view",
            "/etc/passwd");

        using HttpResponseMessage response = await client.PostAsync(
            $"api/ingestions/{ingestionId}/actions/open-folder",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(opener.LastOpenedDirectory);
    }

    private static async Task<long> CreateDownloadedIngestionAsync(HttpClient client, string sourceId, string sourceLink, string downloadedLocation = "/tmp/song.zip")
    {
        string createPayload = JsonSerializer.Serialize(new
        {
            source = "rhythmverse",
            sourceId,
            sourceLink,
            downloadedLocation,
        });

        using HttpResponseMessage createResponse = await client.PostAsync(
            "api/ingestions",
            new StringContent(createPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        string createBody = await createResponse.Content.ReadAsStringAsync();
        using var createJson = JsonDocument.Parse(createBody);
        return createJson.RootElement.GetProperty("ingestionId").GetInt64();
    }

    private static string CreateTestZip(string rootPath, string fileName)
    {
        Directory.CreateDirectory(rootPath);
        string zipPath = Path.Combine(rootPath, fileName);
        using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("song/chart.mid");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("test-midi-data");
        return zipPath;
    }

    private static IngestionSyncApiHost CreateHost(
        string rootPath,
        string token,
        string pairCode = "PAIR-1234",
        string? pairCodeIssuedAtUtc = null,
        int pairCodeTtlMinutes = 10,
        bool allowStateOverride = false,
        int syncApiMaxRequestBodyBytes = 64 * 1024,
        int syncApiBodyReadTimeoutMs = 1000,
        int syncApiMutationWaitTimeoutMs = 250,
        int syncApiSlowRequestThresholdMs = 500,
        IDesktopPathOpener? opener = null,
        string? listenPrefix = null,
        string? advertisedBaseUrl = null)
    {
        (IngestionSyncApiHost? host, AppGlobalSettings _) = CreateHostWithSettings(
            rootPath,
            token,
            pairCode,
            pairCodeIssuedAtUtc,
            pairCodeTtlMinutes,
            allowStateOverride,
            syncApiMaxRequestBodyBytes,
            syncApiBodyReadTimeoutMs,
            syncApiMutationWaitTimeoutMs,
            syncApiSlowRequestThresholdMs,
            opener,
            listenPrefix,
            advertisedBaseUrl);

        return host;
    }

    private static (IngestionSyncApiHost Host, AppGlobalSettings Settings) CreateHostWithSettings(
        string rootPath,
        string token,
        string pairCode = "PAIR-1234",
        string? pairCodeIssuedAtUtc = null,
        int pairCodeTtlMinutes = 10,
        bool allowStateOverride = false,
        int syncApiMaxRequestBodyBytes = 64 * 1024,
        int syncApiBodyReadTimeoutMs = 1000,
        int syncApiMutationWaitTimeoutMs = 250,
        int syncApiSlowRequestThresholdMs = 500,
        IDesktopPathOpener? opener = null,
        string? listenPrefix = null,
        string? advertisedBaseUrl = null)
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
                SyncApiPairCode = pairCode,
                SyncApiPairCodeIssuedAtUtc = pairCodeIssuedAtUtc ?? DateTimeOffset.UtcNow.ToString("O"),
                SyncApiPairCodeTtlMinutes = pairCodeTtlMinutes,
                AllowSyncApiStateOverride = allowStateOverride,
                SyncApiMaxRequestBodyBytes = syncApiMaxRequestBodyBytes,
                SyncApiBodyReadTimeoutMs = syncApiBodyReadTimeoutMs,
                SyncApiMutationWaitTimeoutMs = syncApiMutationWaitTimeoutMs,
                SyncApiSlowRequestThresholdMs = syncApiSlowRequestThresholdMs,
                SyncApiListenPrefix = listenPrefix ?? "http://127.0.0.1:15123/",
                SyncApiAdvertisedBaseUrl = advertisedBaseUrl ?? string.Empty,
            },
        };

        foreach (string? dir in new[]
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

        var installer = new SongInstallService(
            settings,
            catalog,
            stateMachine,
            new OnyxService(settings),
            new SongIniMetadataParser(),
            new CloneHeroDirectorySchemaService(),
            libraryCatalog: null);
        var host = new IngestionSyncApiHost(
            catalog,
            stateMachine,
            settings,
            installer,
            opener ?? new FakeDesktopPathOpener(),
            new FakeSyncLanDiscoveryService());
        return (host, settings);
    }

    private static HttpClient CreateHttpClient()
    {
        return CreateHttpClient(BaseUri);
    }

    private static HttpClient CreateHttpClient(Uri baseUri)
    {
        return new HttpClient
        {
            BaseAddress = baseUri,
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

    private sealed class FakeSyncLanDiscoveryService : ISyncLanDiscoveryService
    {
        public Task<IReadOnlyList<SyncDiscoveryEndpoint>> DiscoverAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SyncDiscoveryEndpoint>>([]);
        }

        public Task StartAdvertisingAsync(string baseUrl, string deviceLabel, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task StopAdvertisingAsync(CancellationToken cancellationToken = default)
        {
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

    [Fact]
    public async Task CreateIngestion_WithLibrarySource_AppearsInPostResponseMetadata()
    {
        using var temp = new TemporaryDirectoryFixture("sync-api-library-source-post");
        await using IngestionSyncApiHost host = CreateHost(temp.RootPath, "token-lib-source");
        await host.StartAsync();

        using HttpClient client = CreateHttpClient();
        client.DefaultRequestHeaders.Add("X-ChartHub-Sync-Token", "token-lib-source");

        string createPayload = JsonSerializer.Serialize(new
        {
            source = "rhythmverse",
            sourceId = "drive-id-lib-source",
            sourceLink = "https://drive.google.com/file/d/libsrc/view",
            artist = "Nirvana",
            title = "Smells Like Teen Spirit",
            charter = "SomeCharter",
            librarySource = "encore",
        });

        using HttpResponseMessage createResponse = await client.PostAsync(
            "api/ingestions",
            new StringContent(createPayload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

        string body = await createResponse.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        JsonElement metadata = json.RootElement.GetProperty("metadata");
        Assert.Equal("encore", metadata.GetProperty("librarySource").GetString());
        Assert.Equal("Nirvana", metadata.GetProperty("artist").GetString());
    }
}
