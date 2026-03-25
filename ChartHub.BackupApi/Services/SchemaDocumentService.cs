using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChartHub.BackupApi.Services;

public sealed class SchemaDocumentService : ISchemaDocumentService
{
    public string BuildJsonSchema()
    {
        JsonObject schema = new()
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://charthub.local/schemas/rhythmverse-song-list-response.json",
            ["title"] = "RhythmVerseSongListResponse",
            ["type"] = "object",
            ["required"] = new JsonArray("status", "data"),
            ["properties"] = new JsonObject
            {
                ["status"] = new JsonObject
                {
                    ["type"] = "string",
                },
                ["data"] = new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("records", "pagination", "songs"),
                    ["properties"] = new JsonObject
                    {
                        ["records"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["required"] = new JsonArray("total_available", "total_filtered", "returned"),
                            ["properties"] = new JsonObject
                            {
                                ["total_available"] = new JsonObject { ["type"] = "integer" },
                                ["total_filtered"] = new JsonObject { ["type"] = "integer" },
                                ["returned"] = new JsonObject { ["type"] = "integer" },
                            },
                            ["additionalProperties"] = true,
                        },
                        ["pagination"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["required"] = new JsonArray("start", "records", "page"),
                            ["properties"] = new JsonObject
                            {
                                ["start"] = new JsonObject { ["type"] = "integer" },
                                ["records"] = new JsonObject { ["type"] = "integer" },
                                ["page"] = new JsonObject { ["type"] = "integer" },
                            },
                            ["additionalProperties"] = true,
                        },
                        ["songs"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["required"] = new JsonArray("data", "file"),
                                ["properties"] = new JsonObject
                                {
                                    ["data"] = new JsonObject
                                    {
                                        ["type"] = "object",
                                        ["additionalProperties"] = true,
                                    },
                                    ["file"] = new JsonObject
                                    {
                                        ["type"] = "object",
                                        ["additionalProperties"] = true,
                                    },
                                },
                                ["additionalProperties"] = true,
                            },
                        },
                    },
                    ["additionalProperties"] = true,
                },
            },
            ["additionalProperties"] = false,
        };

        return schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public string BuildOpenApiComponentsSchema()
    {
        JsonNode jsonSchemaNode = JsonNode.Parse(BuildJsonSchema()) ?? new JsonObject();

        JsonObject openApi = new()
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject
            {
                ["title"] = "ChartHub Backup API Schema",
                ["version"] = "v1",
            },
            ["components"] = new JsonObject
            {
                ["schemas"] = new JsonObject
                {
                    ["RhythmVerseSongListResponse"] = jsonSchemaNode,
                },
            },
        };

        return openApi.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
