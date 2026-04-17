using System.Net;

using ChartHub.BackupApi.Tests.TestInfrastructure;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ApiKeyMiddlewareTests
{
    [Fact]
    public async Task Request_WithoutApiKey_Returns401()
    {
        using BackupApiWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove("X-Api-Key");

        HttpResponseMessage response = await client.GetAsync("/api/rhythmverse/songs/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithWrongApiKey_Returns401()
    {
        using BackupApiWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove("X-Api-Key");
        client.DefaultRequestHeaders.Add("X-Api-Key", "definitely-wrong-key");

        HttpResponseMessage response = await client.GetAsync("/api/rhythmverse/songs/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithCorrectApiKey_PassesThrough()
    {
        using BackupApiWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        // Any non-401 status proves the middleware allowed the request through.
        HttpResponseMessage response = await client.GetAsync("/api/rhythmverse/songs/1");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_WithoutApiKey_Returns200()
    {
        using BackupApiWebApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove("X-Api-Key");

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
