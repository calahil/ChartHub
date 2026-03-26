using System.Net.Http.Json;
using System.Text.Json;

using ChartHub.BackupApi.Tests.TestInfrastructure;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public sealed class SwaggerDocumentationTests : IClassFixture<BackupApiWebApplicationFactory>
{
    private readonly BackupApiWebApplicationFactory _factory;

    public SwaggerDocumentationTests(BackupApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SwaggerJson_AllMappedEndpoints_HaveSummaryAndDescription()
    {
        HttpClient client = _factory.CreateClient();
        JsonElement swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        JsonElement paths = swagger.GetProperty("paths");

        AssertOperationHasSummaryAndDescription(paths, "/health", "get");
        AssertOperationHasSummaryAndDescription(paths, "/api/rhythmverse/songs/{songId}", "get");
        AssertOperationHasSummaryAndDescription(paths, "/api/rhythmverse/download/{fileId}", "get");
        AssertOperationHasSummaryAndDescription(paths, "/api/rhythmverse/health/sync", "get");
        AssertOperationHasSummaryAndDescription(paths, "/api/all/songfiles/list", "post");
        AssertOperationHasSummaryAndDescription(paths, "/api/all/songfiles/search/live", "post");
        AssertOperationHasSummaryAndDescription(paths, "/api/schemas/rhythmverse-song-list.json", "get");
        AssertOperationHasSummaryAndDescription(paths, "/api/schemas/rhythmverse-song-list.openapi.json", "get");
    }

    [Fact]
    public async Task SwaggerJson_CompatibilityEndpoints_DocumentFormDataKeys()
    {
        HttpClient client = _factory.CreateClient();
        JsonElement swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        JsonElement properties = GetFormSchemaProperties(swagger, "/api/all/songfiles/list");

        Assert.True(properties.TryGetProperty("page", out _));
        Assert.True(properties.TryGetProperty("records", out _));
        Assert.True(properties.TryGetProperty("author", out _));
        Assert.True(properties.TryGetProperty("instrument", out _));
        Assert.True(properties.TryGetProperty("sort[0][sort_by]", out _));
        Assert.True(properties.TryGetProperty("sort[0][sort_order]", out _));
        Assert.True(properties.TryGetProperty("text", out _));
        Assert.True(properties.TryGetProperty("data_type", out _));
    }

    private static JsonElement GetFormSchemaProperties(JsonElement swagger, string route)
    {
        JsonElement schema = swagger
            .GetProperty("paths")
            .GetProperty(route)
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/x-www-form-urlencoded")
            .GetProperty("schema");

        if (schema.TryGetProperty("properties", out JsonElement inlineProperties))
        {
            return inlineProperties;
        }

        string reference = schema.GetProperty("$ref").GetString() ?? string.Empty;
        string schemaName = reference.Split('/').LastOrDefault() ?? string.Empty;

        return swagger
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName)
            .GetProperty("properties");
    }

    private static void AssertOperationHasSummaryAndDescription(JsonElement paths, string route, string method)
    {
        JsonElement operation = paths
            .GetProperty(route)
            .GetProperty(method);

        string? summary = operation.GetProperty("summary").GetString();
        string? description = operation.GetProperty("description").GetString();

        Assert.False(string.IsNullOrWhiteSpace(summary));
        Assert.False(string.IsNullOrWhiteSpace(description));
    }
}
