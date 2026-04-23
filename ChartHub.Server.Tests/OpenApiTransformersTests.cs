using System.Text.Json.Nodes;

using ChartHub.Server.OpenApi;

using Microsoft.OpenApi;

namespace ChartHub.Server.Tests;

public sealed class OpenApiTransformersTests
{
    [Fact]
    public async Task DocumentTransformerAddsDownloadStatusExamples()
    {
        OpenApiDocument document = new()
        {
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, OpenApiSchema>
                {
                    ["DownloadJobStatus"] = new OpenApiSchema(),
                    ["DownloadJobResponse"] = new OpenApiSchema
                    {
                        Properties = new Dictionary<string, OpenApiSchema>
                        {
                            ["conversionStatuses"] = new OpenApiSchema(),
                        }.ToDictionary(pair => pair.Key, pair => (IOpenApiSchema)pair.Value),
                    },
                }.ToDictionary(pair => pair.Key, pair => (IOpenApiSchema)pair.Value),
            },
        };

        ChartHubDocumentTransformer sut = new();

        await sut.TransformAsync(document, context: null!, CancellationToken.None);

        OpenApiSchema statusSchema = Assert.IsType<OpenApiSchema>(document.Components.Schemas["DownloadJobStatus"]);
        JsonObject statusExample = Assert.IsType<JsonObject>(statusSchema.Example);
        Assert.Equal("audio-incomplete", statusExample["code"]?.GetValue<string>());

        OpenApiSchema responseSchema = Assert.IsType<OpenApiSchema>(document.Components.Schemas["DownloadJobResponse"]);
        JsonObject responseExample = Assert.IsType<JsonObject>(responseSchema.Example);
        JsonArray conversionStatuses = Assert.IsType<JsonArray>(responseExample["conversionStatuses"]);
        JsonObject firstStatus = Assert.IsType<JsonObject>(Assert.Single(conversionStatuses)!);
        Assert.Equal("Only backing audio was produced.", firstStatus["message"]?.GetValue<string>());

        Assert.NotNull(responseSchema.Properties);
        OpenApiSchema conversionStatusesSchema = Assert.IsType<OpenApiSchema>(responseSchema.Properties!["conversionStatuses"]);
        JsonArray propertyExample = Assert.IsType<JsonArray>(conversionStatusesSchema.Example);
        Assert.Single(propertyExample);
    }
}