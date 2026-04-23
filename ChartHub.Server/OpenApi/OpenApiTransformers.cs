using System.Text.Json.Nodes;

using ChartHub.Server.Middleware;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ChartHub.Server.OpenApi;

/// <summary>
/// Enriches the generated OpenAPI document with license info, server URL, JWT and Runner
/// security scheme definitions, and tag descriptions.
/// </summary>
internal sealed class ChartHubDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info ??= new OpenApiInfo();
        document.Info.License = new OpenApiLicense
        {
            Name = "GNU General Public License v3.0",
            Url = new Uri("https://www.gnu.org/licenses/gpl-3.0.html"),
        };

        // Relative server URL — resolves against the host the client connects to.
        document.Servers = new List<OpenApiServer> { new OpenApiServer { Url = "/" } };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["bearerAuth"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT obtained from `POST /api/v1/auth/exchange`. Pass in the `Authorization: Bearer <token>` header.",
        };

        document.Components.SecuritySchemes["runnerAuth"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "Authorization",
            Description = "Runner credentials in the form `Runner {runnerId}:{secret}`. Issued on runner registration.",
        };

        document.Tags = new HashSet<OpenApiTag>
        {
            new OpenApiTag { Name = "Auth",           Description = "Authentication — exchange a Google ID token for a ChartHub JWT." },
            new OpenApiTag { Name = "CloneHero",      Description = "Clone Hero song library — list, soft-delete, restore, and patch installed songs." },
            new OpenApiTag { Name = "DesktopEntry",   Description = "Desktop-entry management — launch, kill, and stream status of desktop applications." },
            new OpenApiTag { Name = "Downloads",      Description = "Download job lifecycle — create, track, install, and stream song download jobs." },
            new OpenApiTag { Name = "Hud",            Description = "HUD overlay — stream live status updates for the in-game heads-up display." },
            new OpenApiTag { Name = "Input",          Description = "Virtual input devices — WebSocket endpoints for gamepad, touchpad, and keyboard input." },
            new OpenApiTag { Name = "RunnerProtocol", Description = "Transcription runner protocol — registration, heartbeat, job claim, audio download, and result upload." },
            new OpenApiTag { Name = "Runners",        Description = "Transcription runner administration — issue registration tokens, list runners, and deregister." },
            new OpenApiTag { Name = "System",         Description = "Server health and diagnostics." },
            new OpenApiTag { Name = "Transcription",  Description = "Drum transcription — scan, enqueue, retry, review, and approve transcription jobs." },
            new OpenApiTag { Name = "Volume",         Description = "System volume control — read and adjust master volume and per-application audio sessions." },
        };

        AddDownloadStatusExamples(document);

        return Task.CompletedTask;
    }

    private static void AddDownloadStatusExamples(OpenApiDocument document)
    {
        if (document.Components?.Schemas is null)
        {
            return;
        }

        JsonObject audioIncompleteStatusExample = new()
        {
            ["code"] = "audio-incomplete",
            ["message"] = "Only backing audio was produced.",
        };

        if (document.Components.Schemas.TryGetValue("DownloadJobStatus", out IOpenApiSchema? statusSchema)
            && statusSchema is OpenApiSchema concreteStatusSchema)
        {
            concreteStatusSchema.Example = audioIncompleteStatusExample.DeepClone();
        }

        if (document.Components.Schemas.TryGetValue("DownloadJobResponse", out IOpenApiSchema? jobSchema)
            && jobSchema is OpenApiSchema concreteJobSchema)
        {
            if (concreteJobSchema.Properties?.TryGetValue("conversionStatuses", out IOpenApiSchema? conversionStatusesSchema) == true
                && conversionStatusesSchema is OpenApiSchema concreteConversionStatusesSchema)
            {
                concreteConversionStatusesSchema.Example = new JsonArray
                {
                    audioIncompleteStatusExample.DeepClone(),
                };
            }

            concreteJobSchema.Example = new JsonObject
            {
                ["jobId"] = "c4f4a4d8-3bbf-4f8c-a77d-d6458e8d2f49",
                ["source"] = "rhythmverse",
                ["sourceId"] = "rv-example",
                ["displayName"] = "Example Song",
                ["sourceUrl"] = "https://rhythmverse.co/song/example",
                ["stage"] = "Installed",
                ["progressPercent"] = 100,
                ["installedPath"] = "/srv/charthub/songs/Artist/Example Song/Charter__rhythmverse",
                ["installedRelativePath"] = "Artist/Example Song/Charter__rhythmverse",
                ["artist"] = "Artist",
                ["title"] = "Example Song",
                ["charter"] = "Charter",
                ["conversionStatuses"] = new JsonArray
                {
                    audioIncompleteStatusExample.DeepClone(),
                },
                ["drumGenRequested"] = false,
                ["createdAtUtc"] = "2026-04-23T12:00:00Z",
                ["updatedAtUtc"] = "2026-04-23T12:00:03Z",
            };
        }
    }
}

/// <summary>
/// Annotates each operation with its security requirement and common error responses.
/// <list type="bullet">
///   <item>JWT-authenticated operations: <c>bearerAuth</c> security, 401/403 responses.</item>
///   <item>Runner-authenticated operations: <c>runnerAuth</c> security, 401 response.</item>
///   <item>Public operations: explicit empty security (anonymous).</item>
///   <item>All operations: 429 (global rate limiter) and 500 (global exception handler).</item>
/// </list>
/// </summary>
internal sealed class ChartHubOperationTransformer : IOpenApiOperationTransformer
{
    private static readonly OpenApiSecurityRequirement BearerSecurity = new()
    {
        [new OpenApiSecuritySchemeReference("bearerAuth", null!, null!)] = [],
    };

    private static readonly OpenApiSecurityRequirement RunnerSecurity = new()
    {
        [new OpenApiSecuritySchemeReference("runnerAuth", null!, null!)] = [],
    };

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata;
        bool isRunnerAuth = metadata.OfType<RunnerAuthMiddlewareExtensions.RequiresRunnerAuthMetadata>().Any();
        bool isBearerAuth = !isRunnerAuth
            && metadata.OfType<IAuthorizeData>().Any()
            && !metadata.OfType<IAllowAnonymous>().Any();

        operation.Responses ??= new OpenApiResponses();

        if (isRunnerAuth)
        {
            operation.Security = [RunnerSecurity];
            operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Runner credentials missing or invalid." });
        }
        else if (isBearerAuth)
        {
            operation.Security = [BearerSecurity];
            operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Authentication required — Bearer token missing or expired." });
            operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Insufficient permissions." });
        }
        else
        {
            // Explicitly public — satisfies the security-defined rule.
            operation.Security = [];
        }

        // Applied globally by UseRateLimiter().
        operation.Responses.TryAdd("429", new OpenApiResponse { Description = "Rate limit exceeded." });

        // Applied globally by UseExceptionHandler().
        operation.Responses.TryAdd("500", new OpenApiResponse { Description = "Unexpected server error." });

        return Task.CompletedTask;
    }
}
