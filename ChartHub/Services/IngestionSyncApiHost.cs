using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChartHub.Utilities;

namespace ChartHub.Services;

public interface IIngestionSyncApiHost
{
    Task StartAsync(CancellationToken cancellationToken = default);
}

public sealed class IngestionSyncApiHost(
    SongIngestionCatalogService ingestionCatalog,
    SongIngestionStateMachine ingestionStateMachine,
    AppGlobalSettings globalSettings,
    ISongInstallService songInstallService,
    IDesktopPathOpener desktopPathOpener) : IIngestionSyncApiHost, IAsyncDisposable
{
    private readonly SongIngestionCatalogService _ingestionCatalog = ingestionCatalog;
    private readonly SongIngestionStateMachine _ingestionStateMachine = ingestionStateMachine;
    private readonly AppGlobalSettings _globalSettings = globalSettings;
    private readonly ISongInstallService _songInstallService = songInstallService;
    private readonly IDesktopPathOpener _desktopPathOpener = desktopPathOpener;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenLoopTask;
    private bool _started;

    private const string DefaultPrefix = "http://127.0.0.1:15123/";
    private const string SyncTokenHeader = "X-ChartHub-Sync-Token";
    private const string ApiContractVersion = "1.0.0";
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started || OperatingSystem.IsAndroid())
            return Task.CompletedTask;

        _listener.Prefixes.Add(DefaultPrefix);
        _listener.Start();
        _started = true;

        Logger.LogInfo("SyncApi", "Ingestion sync API started", new Dictionary<string, object?>
        {
            ["prefix"] = DefaultPrefix,
            ["authTokenConfigured"] = !string.IsNullOrWhiteSpace(_globalSettings.SyncApiAuthToken),
        });

        _listenLoopTask = Task.Run(() => ListenLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync();
                await HandleRequestAsync(context, cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError("SyncApi", "Sync API request handling failed", ex);
                if (context is not null)
                    await WriteErrorAsync(context.Response, HttpStatusCode.InternalServerError, "Internal server error");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        if (request.Url is null)
        {
            await WriteErrorAsync(response, HttpStatusCode.BadRequest, "Missing URL");
            return;
        }

        var path = request.Url.AbsolutePath.TrimEnd('/');

        if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(response, HttpStatusCode.OK, new { status = "ok" });
            return;
        }

        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            && !IsAuthorized(request, _globalSettings.SyncApiAuthToken))
        {
            response.Headers["WWW-Authenticate"] = "Bearer";
            await WriteErrorAsync(response, HttpStatusCode.Unauthorized, $"Missing or invalid {SyncTokenHeader}");
            return;
        }

        if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.Equals("/api/version", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(response, HttpStatusCode.OK, new
            {
                api = "ingestion-sync",
                version = ApiContractVersion,
                supports = new
                {
                    ingestions = true,
                    events = true,
                    fromStateOverride = true,
                    metadata = true,
                    desktopState = true,
                },
                runtime = new
                {
                    allowSyncApiStateOverride = _globalSettings.AllowSyncApiStateOverride,
                },
            });
            return;
        }

        if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.Equals("/api/ingestions", StringComparison.OrdinalIgnoreCase))
        {
            var state = request.QueryString["state"];
            var source = request.QueryString["source"];
            var sort = request.QueryString["sort"] ?? "Updated";
            var desc = bool.TryParse(request.QueryString["desc"], out var parsedDesc) && parsedDesc;

            var items = await _ingestionCatalog.QueryQueueAsync(state, source, sort, desc, cancellationToken: cancellationToken);
            await WriteJsonAsync(response, HttpStatusCode.OK, new { items });
            return;
        }

        if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && TryParseSingleIngestionEndpoint(path, out var singleIngestionId))
        {
            var item = await _ingestionCatalog.GetQueueItemByIdAsync(singleIngestionId, cancellationToken);
            if (item is null)
            {
                await WriteErrorAsync(response, HttpStatusCode.NotFound, "ingestion not found");
                return;
            }

            await WriteJsonAsync(response, HttpStatusCode.OK, new { item });
            return;
        }

        if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && path.Equals("/api/ingestions", StringComparison.OrdinalIgnoreCase))
        {
            var payload = await JsonSerializer.DeserializeAsync<IngestionSyncRequest>(
                request.InputStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);

            if (payload is null || string.IsNullOrWhiteSpace(payload.Source) || string.IsNullOrWhiteSpace(payload.SourceLink))
            {
                await WriteErrorAsync(response, HttpStatusCode.BadRequest, "source and sourceLink are required");
                return;
            }

            var source = payload.Source.Trim().ToLowerInvariant();
            var canonicalSourceId = LibraryIdentityService.NormalizeSourceKey(source, payload.SourceId);
            var ingestion = await _ingestionCatalog.GetOrCreateIngestionAsync(
                source,
                canonicalSourceId,
                payload.SourceLink,
                payload.Artist,
                payload.Title,
                payload.Charter,
                cancellationToken);
            var attempt = await _ingestionCatalog.StartAttemptAsync(ingestion.Id, cancellationToken);

            var fromState = ingestion.CurrentState;
            var targetIngestionState = string.IsNullOrWhiteSpace(payload.DownloadedLocation)
                ? IngestionState.Queued
                : IngestionState.Downloaded;

            if (fromState != targetIngestionState)
            {
                var targetState = _ingestionStateMachine.CanTransition(fromState, targetIngestionState)
                    ? targetIngestionState
                    : IngestionState.Queued;

                if (targetState == IngestionState.Queued
                    && targetIngestionState != IngestionState.Queued
                    && _ingestionStateMachine.CanTransition(IngestionState.Queued, targetIngestionState))
                {
                    await _ingestionCatalog.RecordStateTransitionAsync(
                        ingestion.Id,
                        attempt.Id,
                        fromState,
                        IngestionState.Queued,
                        "Sync API reset",
                        cancellationToken);

                    fromState = IngestionState.Queued;
                    targetState = targetIngestionState;
                }

                await _ingestionCatalog.RecordStateTransitionAsync(
                    ingestion.Id,
                    attempt.Id,
                    fromState,
                    targetState,
                    "Sync API ingestion update",
                    cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(payload.DownloadedLocation))
            {
                await _ingestionCatalog.UpsertAssetAsync(new SongIngestionAssetEntry(
                    IngestionId: ingestion.Id,
                    AttemptId: attempt.Id,
                    AssetRole: IngestionAssetRole.Downloaded,
                    Location: payload.DownloadedLocation,
                    SizeBytes: payload.SizeBytes,
                    ContentHash: payload.ContentHash,
                    RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
            }

            await WriteJsonAsync(response, HttpStatusCode.Accepted, new
            {
                ingestionId = ingestion.Id,
                normalizedLink = ingestion.NormalizedLink,
                state = targetIngestionState.ToString(),
                metadata = new
                {
                    artist = ingestion.Artist,
                    title = ingestion.Title,
                    charter = ingestion.Charter,
                },
            });
            return;
        }

        if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && TryParseEventsEndpoint(path, out var ingestionId))
        {
            var ingestion = await _ingestionCatalog.GetIngestionByIdAsync(ingestionId, cancellationToken);
            if (ingestion is null)
            {
                await WriteErrorAsync(response, HttpStatusCode.NotFound, "ingestion not found");
                return;
            }

            var payload = await JsonSerializer.DeserializeAsync<IngestionEventRequest>(
                request.InputStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);

            if (payload is null || string.IsNullOrWhiteSpace(payload.ToState))
            {
                await WriteErrorAsync(response, HttpStatusCode.BadRequest, "toState is required");
                return;
            }

            if (!Enum.TryParse<IngestionState>(payload.ToState, ignoreCase: true, out var toState))
            {
                await WriteErrorAsync(response, HttpStatusCode.BadRequest, "Invalid toState");
                return;
            }

            var persistedState = ingestion.CurrentState;
            var fromState = persistedState;
            if (!string.IsNullOrWhiteSpace(payload.FromState))
            {
                if (!Enum.TryParse<IngestionState>(payload.FromState, ignoreCase: true, out var requestFromState))
                {
                    await WriteErrorAsync(response, HttpStatusCode.BadRequest, "Invalid fromState");
                    return;
                }

                var allowOverride = payload.AllowFromStateOverride && _globalSettings.AllowSyncApiStateOverride;
                if (requestFromState != persistedState && !allowOverride)
                {
                    await WriteErrorAsync(response, HttpStatusCode.Conflict, "fromState does not match persisted ingestion state");
                    return;
                }

                fromState = requestFromState;
            }

            if (fromState != toState)
            {
                if (_ingestionStateMachine.CanTransition(fromState, toState))
                {
                    await _ingestionCatalog.RecordStateTransitionAsync(
                        ingestion.Id,
                        attemptId: null,
                        fromState,
                        toState,
                        payload.Details ?? "Sync API event",
                        cancellationToken);
                }
                else if (_ingestionStateMachine.CanTransition(fromState, IngestionState.Queued)
                    && _ingestionStateMachine.CanTransition(IngestionState.Queued, toState))
                {
                    await _ingestionCatalog.RecordStateTransitionAsync(
                        ingestion.Id,
                        attemptId: null,
                        fromState,
                        IngestionState.Queued,
                        "Sync API event reset",
                        cancellationToken);

                    await _ingestionCatalog.RecordStateTransitionAsync(
                        ingestion.Id,
                        attemptId: null,
                        IngestionState.Queued,
                        toState,
                        payload.Details ?? "Sync API event",
                        cancellationToken);
                }
                else
                {
                    await WriteErrorAsync(response, HttpStatusCode.BadRequest, "Invalid ingestion transition");
                    return;
                }
            }

            await WriteJsonAsync(response, HttpStatusCode.Accepted, new
            {
                ingestionId = ingestion.Id,
                fromState = fromState.ToString(),
                toState = toState.ToString(),
            });
            return;
        }

        if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && TryParseActionEndpoint(path, out var actionIngestionId, out var actionName))
        {
            await HandleActionRequestAsync(response, actionIngestionId, actionName, cancellationToken);
            return;
        }

        await WriteErrorAsync(response, HttpStatusCode.NotFound, "Endpoint not found");
    }

    private async Task HandleActionRequestAsync(
        HttpListenerResponse response,
        long ingestionId,
        string actionName,
        CancellationToken cancellationToken)
    {
        var ingestion = await _ingestionCatalog.GetIngestionByIdAsync(ingestionId, cancellationToken);
        if (ingestion is null)
        {
            await WriteErrorAsync(response, HttpStatusCode.NotFound, "ingestion not found");
            return;
        }

        if (actionName.Equals("retry", StringComparison.OrdinalIgnoreCase))
        {
            var fromState = ingestion.CurrentState;
            if (fromState == IngestionState.Queued)
            {
                await WriteJsonAsync(response, HttpStatusCode.Accepted, new
                {
                    ingestionId,
                    action = "retry",
                    state = IngestionState.Queued.ToString(),
                    noop = true,
                });
                return;
            }

            if (!_ingestionStateMachine.CanTransition(fromState, IngestionState.Queued))
            {
                await WriteErrorAsync(response, HttpStatusCode.Conflict, "Retry is not valid for the current ingestion state");
                return;
            }

            await _ingestionCatalog.RecordStateTransitionAsync(
                ingestionId,
                attemptId: null,
                fromState,
                IngestionState.Queued,
                "Sync API action retry",
                cancellationToken);

            await WriteJsonAsync(response, HttpStatusCode.Accepted, new
            {
                ingestionId,
                action = "retry",
                fromState = fromState.ToString(),
                toState = IngestionState.Queued.ToString(),
            });
            return;
        }

        if (actionName.Equals("install", StringComparison.OrdinalIgnoreCase))
        {
            var downloadablePath = await _ingestionCatalog.GetLatestAssetLocationAsync(
                ingestionId,
                IngestionAssetRole.Downloaded,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(downloadablePath))
            {
                await WriteErrorAsync(response, HttpStatusCode.Conflict, "No downloaded asset exists for this ingestion");
                return;
            }

            if (!File.Exists(downloadablePath))
            {
                await WriteErrorAsync(response, HttpStatusCode.Conflict, "Downloaded asset path does not exist on disk");
                return;
            }

            var installedDirectories = await _songInstallService.InstallSelectedDownloadsAsync([downloadablePath], cancellationToken: cancellationToken);
            if (installedDirectories.Count == 0)
            {
                await WriteErrorAsync(response, HttpStatusCode.Conflict, "Install action completed with no installed output");
                return;
            }

            var queueItem = await _ingestionCatalog.GetQueueItemByIdAsync(ingestionId, cancellationToken);
            await WriteJsonAsync(response, HttpStatusCode.Accepted, new
            {
                ingestionId,
                action = "install",
                installedDirectories,
                desktopState = queueItem?.DesktopState.ToString() ?? DesktopState.Cloud.ToString(),
                desktopPath = queueItem?.DesktopLibraryPath,
            });
            return;
        }

        if (actionName.Equals("open-folder", StringComparison.OrdinalIgnoreCase))
        {
            var installedDirectory = await _ingestionCatalog.GetLatestAssetLocationAsync(
                ingestionId,
                IngestionAssetRole.InstalledDirectory,
                cancellationToken);

            var targetDirectory = installedDirectory;
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                var downloadedPath = await _ingestionCatalog.GetLatestAssetLocationAsync(
                    ingestionId,
                    IngestionAssetRole.Downloaded,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(downloadedPath))
                {
                    targetDirectory = Directory.Exists(downloadedPath)
                        ? downloadedPath
                        : Path.GetDirectoryName(downloadedPath);
                }
            }

            if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
            {
                await WriteErrorAsync(response, HttpStatusCode.Conflict, "No openable folder exists for this ingestion");
                return;
            }

            await _desktopPathOpener.OpenDirectoryAsync(targetDirectory, cancellationToken);
            await WriteJsonAsync(response, HttpStatusCode.Accepted, new
            {
                ingestionId,
                action = "open-folder",
                directory = targetDirectory,
            });
            return;
        }

        await WriteErrorAsync(response, HttpStatusCode.NotFound, "Action not found");
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, HttpStatusCode statusCode, object payload)
    {
        var json = JsonSerializer.Serialize(payload, ResponseJsonOptions);
        var buffer = Encoding.UTF8.GetBytes(json);
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

    private static Task WriteErrorAsync(HttpListenerResponse response, HttpStatusCode statusCode, string message)
    {
        return WriteJsonAsync(response, statusCode, new { error = message });
    }

    private static bool TryParseEventsEndpoint(string path, out long ingestionId)
    {
        ingestionId = 0;
        var normalized = path.Trim('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            return false;

        if (!parts[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("ingestions", StringComparison.OrdinalIgnoreCase)
            || !parts[3].Equals("events", StringComparison.OrdinalIgnoreCase))
            return false;

        return long.TryParse(parts[2], out ingestionId);
    }

    private static bool TryParseActionEndpoint(string path, out long ingestionId, out string actionName)
    {
        ingestionId = 0;
        actionName = string.Empty;

        var normalized = path.Trim('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            return false;

        if (!parts[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("ingestions", StringComparison.OrdinalIgnoreCase)
            || !parts[3].Equals("actions", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!long.TryParse(parts[2], out ingestionId))
            return false;

        actionName = parts[4];
        return !string.IsNullOrWhiteSpace(actionName);
    }

    private static bool TryParseSingleIngestionEndpoint(string path, out long ingestionId)
    {
        ingestionId = 0;
        var normalized = path.Trim('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;

        if (!parts[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("ingestions", StringComparison.OrdinalIgnoreCase))
            return false;

        return long.TryParse(parts[2], out ingestionId);
    }

    private static bool IsAuthorized(HttpListenerRequest request, string expectedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken))
            return true;

        var suppliedToken = request.Headers[SyncTokenHeader];
        if (string.IsNullOrWhiteSpace(suppliedToken))
        {
            var authorization = request.Headers["Authorization"];
            if (!string.IsNullOrWhiteSpace(authorization)
                && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                suppliedToken = authorization["Bearer ".Length..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(suppliedToken))
            return false;

        return FixedTimeEquals(expectedToken, suppliedToken);
    }

    private static bool FixedTimeEquals(string expected, string supplied)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected.Trim());
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied.Trim());
        return CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            await _cts.CancelAsync();
            _listener.Close();

            if (_listenLoopTask is not null)
            {
                try
                {
                    await _listenLoopTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            Logger.LogInfo("SyncApi", "Ingestion sync API stopped", new Dictionary<string, object?>
            {
                ["prefix"] = DefaultPrefix,
            });
        }

        _cts.Dispose();
    }

    private sealed class IngestionSyncRequest
    {
        public string Source { get; set; } = string.Empty;
        public string? SourceId { get; set; }
        public string SourceLink { get; set; } = string.Empty;
        public string? DownloadedLocation { get; set; }
        public long? SizeBytes { get; set; }
        public string? ContentHash { get; set; }
        public string? Artist { get; set; }
        public string? Title { get; set; }
        public string? Charter { get; set; }
    }

    private sealed class IngestionEventRequest
    {
        public string? FromState { get; set; }
        public string ToState { get; set; } = string.Empty;
        public string? Details { get; set; }
        public bool AllowFromStateOverride { get; set; }
    }
}
