using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ChartHub.Models;
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
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly object _pairCodeSync = new();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private string _currentPairCode = string.Empty;
    private DateTimeOffset _currentPairCodeIssuedAtUtc = DateTimeOffset.MinValue;
    private long _requestsTotal;
    private long _slowRequestsTotal;
    private long _busyMutationRejectionsTotal;
    private long _clientErrorsTotal;
    private long _serverErrorsTotal;
    private Task? _listenLoopTask;
    private bool _started;
    private string _activeListenerPrefix = DefaultListenPrefix;
    private string _activeApiBaseUrl = DefaultListenPrefix.TrimEnd('/');

    private const string DefaultListenPrefix = "http://0.0.0.0:15123/";
    private const string SyncTokenHeader = "X-ChartHub-Sync-Token";
    private const string ApiContractVersion = "1.0.0";
    private const int MaxQueryLimit = 500;
    private const int MaxPairingHistoryEntries = 10;
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private int MaxRequestBodyBytes => _globalSettings.SyncApiMaxRequestBodyBytes;
    private TimeSpan MaxMutationWaitDuration => TimeSpan.FromMilliseconds(_globalSettings.SyncApiMutationWaitTimeoutMs);
    private TimeSpan MaxRequestBodyReadDuration => TimeSpan.FromMilliseconds(_globalSettings.SyncApiBodyReadTimeoutMs);
    private TimeSpan SlowRequestThreshold => TimeSpan.FromMilliseconds(_globalSettings.SyncApiSlowRequestThresholdMs);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started || OperatingSystem.IsAndroid())
        {
            return Task.CompletedTask;
        }

        _activeListenerPrefix = DefaultListenPrefix;
        _activeApiBaseUrl = ResolveConfiguredApiBaseUrl(DefaultListenPrefix);
        string listenerPrefix = ToHttpListenerPrefix(_activeListenerPrefix);

        _listener.Prefixes.Add(listenerPrefix);
        _listener.Start();
        _started = true;

        Logger.LogInfo("SyncApi", "Ingestion sync API started", new Dictionary<string, object?>
        {
            ["prefix"] = _activeListenerPrefix,
            ["apiBaseUrl"] = _activeApiBaseUrl,
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
                Logger.LogError("SyncApi", "Sync API request handling failed", ex, new Dictionary<string, object?>
                {
                    ["method"] = context?.Request?.HttpMethod,
                    ["path"] = context?.Request?.Url?.AbsolutePath,
                });
                if (context is not null)
                {
                    await WriteErrorAsync(context.Response, HttpStatusCode.InternalServerError, "Internal server error");
                }
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (request.Url is null)
            {
                await WriteErrorAsync(response, HttpStatusCode.BadRequest, "Missing URL");
                return;
            }

            string path = request.Url.AbsolutePath.TrimEnd('/');

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase)
                && path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(response, HttpStatusCode.OK, new { status = "ok" });
                return;
            }

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                && IsPairClaimEndpoint(path))
            {
                PairClaimRequest? payload = await TryReadJsonBodyAsync<PairClaimRequest>(request, response, cancellationToken);
                if (payload is null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(payload.PairCode))
                {
                    await WriteErrorAsync(response, HttpStatusCode.BadRequest, "pairCode is required");
                    return;
                }

                string configuredPairCode = GetCurrentPairCode();
                if (string.IsNullOrWhiteSpace(configuredPairCode))
                {
                    await WriteErrorAsync(response, HttpStatusCode.Conflict, "Desktop pairing code is not configured");
                    return;
                }

                if (IsPairCodeExpired())
                {
                    await WriteErrorAsync(response, HttpStatusCode.Gone, "Pair code expired");
                    return;
                }

                if (!FixedTimeEquals(configuredPairCode, payload.PairCode.Trim()))
                {
                    await WriteErrorAsync(response, HttpStatusCode.Unauthorized, "Invalid pair code");
                    return;
                }

                RotatePairCode();

                string deviceLabel = payload.DeviceLabel?.Trim() ?? string.Empty;
                DateTimeOffset pairedAtUtc = DateTimeOffset.UtcNow;

                _globalSettings.SyncApiLastPairedDeviceLabel = deviceLabel;
                _globalSettings.SyncApiLastPairedAtUtc = pairedAtUtc.ToString("O");
                AppendPairingHistory(deviceLabel, pairedAtUtc);

                Logger.LogInfo("SyncApi", "Companion device paired", new Dictionary<string, object?>
                {
                    ["deviceLabel"] = payload.DeviceLabel,
                    ["path"] = request.Url.AbsolutePath,
                });

                await WriteJsonAsync(response, HttpStatusCode.OK, new
                {
                    paired = true,
                    token = _globalSettings.SyncApiAuthToken,
                    apiBaseUrl = ResolveRequestApiBaseUrl(request),
                    pairedAtUtc,
                });
                return;
            }

            if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
                && !IsPairClaimEndpoint(path)
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
                        // Keep both keys during contract transition for compatibility with existing clients.
                        desktopLibraryStatus = true,
                        desktopState = true,
                    },
                    runtime = new
                    {
                        allowSyncApiStateOverride = _globalSettings.AllowSyncApiStateOverride,
                        pairCodeTtlMinutes = _globalSettings.SyncApiPairCodeTtlMinutes,
                        maxRequestBodyBytes = _globalSettings.SyncApiMaxRequestBodyBytes,
                        bodyReadTimeoutMs = _globalSettings.SyncApiBodyReadTimeoutMs,
                        mutationWaitTimeoutMs = _globalSettings.SyncApiMutationWaitTimeoutMs,
                        slowRequestThresholdMs = _globalSettings.SyncApiSlowRequestThresholdMs,
                        telemetry = new
                        {
                            startedAtUtc = _startedAtUtc,
                            requestsTotal = Interlocked.Read(ref _requestsTotal),
                            slowRequestsTotal = Interlocked.Read(ref _slowRequestsTotal),
                            busyMutationRejectionsTotal = Interlocked.Read(ref _busyMutationRejectionsTotal),
                            clientErrorsTotal = Interlocked.Read(ref _clientErrorsTotal),
                            serverErrorsTotal = Interlocked.Read(ref _serverErrorsTotal),
                        },
                    },
                });
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase)
                && path.Equals("/api/ingestions", StringComparison.OrdinalIgnoreCase))
            {
                string? state = request.QueryString["state"];
                string? source = request.QueryString["source"];
                if (!string.IsNullOrWhiteSpace(source) && !string.Equals(source, "all", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        source = LibrarySourceNames.NormalizeTrustedSource(source);
                    }
                    catch (ArgumentException)
                    {
                        await WriteErrorAsync(response, HttpStatusCode.BadRequest, "source must be rhythmverse or encore");
                        return;
                    }
                }

                string sort = request.QueryString["sort"] ?? "Updated";
                bool desc = bool.TryParse(request.QueryString["desc"], out bool parsedDesc) && parsedDesc;
                int limit = ParseLimit(request.QueryString["limit"]);

                IReadOnlyList<IngestionQueueItem> items = await _ingestionCatalog.QueryQueueAsync(state, source, sort, desc, limit, cancellationToken);
                await WriteJsonAsync(response, HttpStatusCode.OK, new { items });
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase)
                && TryParseSingleIngestionEndpoint(path, out long singleIngestionId))
            {
                IngestionQueueItem? item = await _ingestionCatalog.GetQueueItemByIdAsync(singleIngestionId, cancellationToken);
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
                await ExecuteSerializedMutationAsync(async () =>
                {
                    IngestionSyncRequest? payload = await TryReadJsonBodyAsync<IngestionSyncRequest>(request, response, cancellationToken);
                    if (payload is null)
                    {
                        return;
                    }

                    if (payload is null || string.IsNullOrWhiteSpace(payload.Source) || string.IsNullOrWhiteSpace(payload.SourceLink))
                    {
                        await WriteErrorAsync(response, HttpStatusCode.BadRequest, "source and sourceLink are required");
                        return;
                    }

                    string source;
                    try
                    {
                        source = LibrarySourceNames.NormalizeTrustedSource(payload.Source);
                    }
                    catch (ArgumentException)
                    {
                        await WriteErrorAsync(response, HttpStatusCode.BadRequest, "source must be rhythmverse or encore");
                        return;
                    }

                    string? librarySource = null;
                    if (!string.IsNullOrWhiteSpace(payload.LibrarySource))
                    {
                        try
                        {
                            librarySource = LibrarySourceNames.NormalizeTrustedSource(payload.LibrarySource);
                        }
                        catch (ArgumentException)
                        {
                            await WriteErrorAsync(response, HttpStatusCode.BadRequest, "librarySource must be rhythmverse or encore");
                            return;
                        }
                    }

                    string canonicalSourceId = LibraryIdentityService.NormalizeSourceKey(source, payload.SourceId);
                    SongIngestionRecord ingestion = await _ingestionCatalog.GetOrCreateIngestionAsync(
                        source,
                        canonicalSourceId,
                        payload.SourceLink,
                        payload.Artist,
                        payload.Title,
                        payload.Charter,
                        cancellationToken,
                        librarySource);
                    SongIngestionAttemptRecord attempt = await _ingestionCatalog.StartAttemptAsync(ingestion.Id, cancellationToken);

                    IngestionState fromState = ingestion.CurrentState;
                    IngestionState targetIngestionState = string.IsNullOrWhiteSpace(payload.DownloadedLocation)
                        ? IngestionState.Queued
                        : IngestionState.Downloaded;

                    if (fromState != targetIngestionState)
                    {
                        IngestionState targetState = _ingestionStateMachine.CanTransition(fromState, targetIngestionState)
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
                            librarySource = ingestion.LibrarySource,
                        },
                    });
                }, request, response, cancellationToken);
                return;
            }

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                && TryParseEventsEndpoint(path, out long ingestionId))
            {
                await ExecuteSerializedMutationAsync(async () =>
                {
                    SongIngestionRecord? ingestion = await _ingestionCatalog.GetIngestionByIdAsync(ingestionId, cancellationToken);
                    if (ingestion is null)
                    {
                        await WriteErrorAsync(response, HttpStatusCode.NotFound, "ingestion not found");
                        return;
                    }

                    IngestionEventRequest? payload = await TryReadJsonBodyAsync<IngestionEventRequest>(request, response, cancellationToken);
                    if (payload is null)
                    {
                        return;
                    }

                    if (payload is null || string.IsNullOrWhiteSpace(payload.ToState))
                    {
                        await WriteErrorAsync(response, HttpStatusCode.BadRequest, "toState is required");
                        return;
                    }

                    if (!Enum.TryParse<IngestionState>(payload.ToState, ignoreCase: true, out IngestionState toState))
                    {
                        await WriteErrorAsync(response, HttpStatusCode.BadRequest, "Invalid toState");
                        return;
                    }

                    IngestionState persistedState = ingestion.CurrentState;
                    IngestionState fromState = persistedState;
                    if (!string.IsNullOrWhiteSpace(payload.FromState))
                    {
                        if (!Enum.TryParse<IngestionState>(payload.FromState, ignoreCase: true, out IngestionState requestFromState))
                        {
                            await WriteErrorAsync(response, HttpStatusCode.BadRequest, "Invalid fromState");
                            return;
                        }

                        bool allowOverride = payload.AllowFromStateOverride && _globalSettings.AllowSyncApiStateOverride;
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
                }, request, response, cancellationToken);
                return;
            }

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                && TryParseActionEndpoint(path, out long actionIngestionId, out string? actionName))
            {
                await ExecuteSerializedMutationAsync(
                    () => HandleActionRequestAsync(response, actionIngestionId, actionName, cancellationToken),
                    request,
                    response,
                    cancellationToken);
                return;
            }

            await WriteErrorAsync(response, HttpStatusCode.NotFound, "Endpoint not found");
        }
        finally
        {
            LogRequestCompletion(request, response, stopwatch.Elapsed);
        }
    }

    private async Task HandleActionRequestAsync(
        HttpListenerResponse response,
        long ingestionId,
        string actionName,
        CancellationToken cancellationToken)
    {
        string correlationId = Guid.NewGuid().ToString("N");
        SongIngestionRecord? ingestion = await _ingestionCatalog.GetIngestionByIdAsync(ingestionId, cancellationToken);
        if (ingestion is null)
        {
            await WriteErrorAsync(response, HttpStatusCode.NotFound, "ingestion not found");
            return;
        }

        if (actionName.Equals("retry", StringComparison.OrdinalIgnoreCase))
        {
            IngestionState fromState = ingestion.CurrentState;
            if (fromState == IngestionState.Queued)
            {
                await WriteJsonAsync(response, HttpStatusCode.Accepted, new
                {
                    ingestionId,
                    action = "retry",
                    state = IngestionState.Queued.ToString(),
                    noop = true,
                    correlationId,
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
                correlationId,
            });
            return;
        }

        if (actionName.Equals("install", StringComparison.OrdinalIgnoreCase))
        {
            string? downloadablePath = await _ingestionCatalog.GetLatestAssetLocationAsync(
                ingestionId,
                IngestionAssetRole.Downloaded,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(downloadablePath))
            {
                await WriteErrorAsync(response, HttpStatusCode.Conflict, "No downloaded asset exists for this ingestion");
                return;
            }

            if (!IsWithinManagedRoots(downloadablePath, expectDirectory: false))
            {
                await WriteErrorAsync(response, HttpStatusCode.Conflict, "Downloaded asset path is outside managed directories");
                return;
            }

            if (!File.Exists(downloadablePath))
            {
                await WriteErrorAsync(response, HttpStatusCode.Conflict, "Downloaded asset path does not exist on disk");
                return;
            }

            IReadOnlyList<string> installedDirectories = await _songInstallService.InstallSelectedDownloadsAsync([downloadablePath], cancellationToken: cancellationToken);
            if (installedDirectories.Count == 0)
            {
                await WriteErrorAsync(response, HttpStatusCode.Conflict, "Install action completed with no installed output");
                return;
            }

            IngestionQueueItem? queueItem = await _ingestionCatalog.GetQueueItemByIdAsync(ingestionId, cancellationToken);
            await WriteJsonAsync(response, HttpStatusCode.Accepted, new
            {
                ingestionId,
                action = "install",
                installedDirectories,
                desktopState = queueItem?.DesktopState.ToString() ?? DesktopState.Cloud.ToString(),
                desktopPath = queueItem?.DesktopLibraryPath,
                correlationId,
            });
            return;
        }

        if (actionName.Equals("open-folder", StringComparison.OrdinalIgnoreCase))
        {
            string? installedDirectory = await _ingestionCatalog.GetLatestAssetLocationAsync(
                ingestionId,
                IngestionAssetRole.InstalledDirectory,
                cancellationToken);

            string? targetDirectory = installedDirectory;
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                string? downloadedPath = await _ingestionCatalog.GetLatestAssetLocationAsync(
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

            if (!IsWithinManagedRoots(targetDirectory, expectDirectory: true))
            {
                await WriteErrorAsync(response, HttpStatusCode.Conflict, "Open-folder target is outside managed directories");
                return;
            }

            await _desktopPathOpener.OpenDirectoryAsync(targetDirectory, cancellationToken);
            await WriteJsonAsync(response, HttpStatusCode.Accepted, new
            {
                ingestionId,
                action = "open-folder",
                directory = targetDirectory,
                correlationId,
            });
            return;
        }

        await WriteErrorAsync(response, HttpStatusCode.NotFound, "Action not found");
    }

    private bool IsWithinManagedRoots(string candidatePath, bool expectDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        string resolvedPath;
        try
        {
            resolvedPath = Path.GetFullPath(candidatePath);
            if (expectDirectory)
            {
                resolvedPath = resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
        catch
        {
            return false;
        }

        return GetManagedRoots().Any(root => IsWithinRoot(root, resolvedPath));
    }

    private IEnumerable<string> GetManagedRoots()
    {
        string[] roots = new[]
        {
            _globalSettings.TempDir,
            _globalSettings.DownloadDir,
            _globalSettings.StagingDir,
            _globalSettings.OutputDir,
            _globalSettings.CloneHeroSongsDir,
        };

        return roots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct();
    }

    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (candidatePath.Equals(rootPath, comparison))
        {
            return true;
        }

        string normalizedRoot = rootPath + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(normalizedRoot, comparison);
    }

    private static int ParseLimit(string? limitValue)
    {
        if (!int.TryParse(limitValue, out int parsedLimit) || parsedLimit <= 0)
        {
            return MaxQueryLimit;
        }

        return Math.Min(parsedLimit, MaxQueryLimit);
    }

    private async Task<T?> TryReadJsonBodyAsync<T>(
        HttpListenerRequest request,
        HttpListenerResponse response,
        CancellationToken cancellationToken) where T : class
    {
        if (string.IsNullOrWhiteSpace(request.ContentType)
            || !request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            await WriteErrorAsync(response, HttpStatusCode.UnsupportedMediaType, "Content-Type must be application/json");
            return null;
        }

        if (request.ContentLength64 > MaxRequestBodyBytes)
        {
            await WriteErrorAsync(response, HttpStatusCode.RequestEntityTooLarge, $"Request body exceeds {MaxRequestBodyBytes} bytes");
            return null;
        }

        using var readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readTimeoutCts.CancelAfter(MaxRequestBodyReadDuration);

        await using var bodyBuffer = new MemoryStream();
        byte[] buffer = new byte[8192];
        try
        {
            while (true)
            {
                int bytesRead = await request.InputStream.ReadAsync(buffer, readTimeoutCts.Token);
                if (bytesRead == 0)
                {
                    break;
                }

                if (bodyBuffer.Length + bytesRead > MaxRequestBodyBytes)
                {
                    await WriteErrorAsync(response, HttpStatusCode.RequestEntityTooLarge, $"Request body exceeds {MaxRequestBodyBytes} bytes");
                    return null;
                }

                await bodyBuffer.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await WriteErrorAsync(response, HttpStatusCode.RequestTimeout, "Request body read timed out");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(bodyBuffer.GetBuffer().AsSpan(0, (int)bodyBuffer.Length), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException)
        {
            await WriteErrorAsync(response, HttpStatusCode.BadRequest, "Invalid JSON body");
            return null;
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, HttpStatusCode statusCode, object payload)
    {
        try
        {
            string json = JsonSerializer.Serialize(payload, ResponseJsonOptions);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.StatusCode = (int)statusCode;
            response.ContentType = "application/json";
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer);
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch
            {
                // Best-effort close; caller handles request-level failures.
            }
        }
    }

    private static Task WriteErrorAsync(HttpListenerResponse response, HttpStatusCode statusCode, string message)
    {
        return WriteJsonAsync(response, statusCode, new { error = message });
    }

    private static bool TryParseEventsEndpoint(string path, out long ingestionId)
    {
        ingestionId = 0;
        string normalized = path.Trim('/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        if (!parts[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("ingestions", StringComparison.OrdinalIgnoreCase)
            || !parts[3].Equals("events", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return long.TryParse(parts[2], out ingestionId);
    }

    private static bool TryParseActionEndpoint(string path, out long ingestionId, out string actionName)
    {
        ingestionId = 0;
        actionName = string.Empty;

        string normalized = path.Trim('/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
        {
            return false;
        }

        if (!parts[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("ingestions", StringComparison.OrdinalIgnoreCase)
            || !parts[3].Equals("actions", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!long.TryParse(parts[2], out ingestionId))
        {
            return false;
        }

        actionName = parts[4];
        return !string.IsNullOrWhiteSpace(actionName);
    }

    private static bool TryParseSingleIngestionEndpoint(string path, out long ingestionId)
    {
        ingestionId = 0;
        string normalized = path.Trim('/');
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!parts[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("ingestions", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return long.TryParse(parts[2], out ingestionId);
    }

    private static bool IsPairClaimEndpoint(string path)
    {
        return path.Equals("/api/pair/claim", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToHttpListenerPrefix(string listenPrefix)
    {
        if (!Uri.TryCreate(listenPrefix, UriKind.Absolute, out Uri? parsed))
        {
            return listenPrefix;
        }

        if (string.Equals(parsed.Host, "0.0.0.0", StringComparison.Ordinal))
        {
            string prefix = $"{parsed.Scheme}://+:{parsed.Port}/";
            return prefix;
        }

        return listenPrefix;
    }

    private static string ResolveConfiguredApiBaseUrl(string listenerPrefix)
    {
        return SyncApiAddressResolver.ResolveAdvertisedApiBaseUrl(listenerPrefix, string.Empty);
    }

    private string ResolveRequestApiBaseUrl(HttpListenerRequest request)
    {
        if (!string.IsNullOrWhiteSpace(_activeApiBaseUrl))
        {
            return _activeApiBaseUrl;
        }

        if (request.Url is not null)
        {
            string fromRequest = request.Url.GetLeftPart(UriPartial.Authority);
            if (!string.IsNullOrWhiteSpace(fromRequest))
            {
                return fromRequest;
            }
        }

        return DefaultListenPrefix.TrimEnd('/');
    }

    private string GetCurrentPairCode()
    {
        lock (_pairCodeSync)
        {
            if (!string.IsNullOrWhiteSpace(_currentPairCode))
            {
                return _currentPairCode;
            }

            _currentPairCode = _globalSettings.SyncApiPairCode.Trim();
            _currentPairCodeIssuedAtUtc = TryParseIssuedAt(_globalSettings.SyncApiPairCodeIssuedAtUtc);
            return _currentPairCode;
        }
    }

    private bool IsPairCodeExpired()
    {
        lock (_pairCodeSync)
        {
            if (string.IsNullOrWhiteSpace(_currentPairCode))
            {
                _currentPairCode = _globalSettings.SyncApiPairCode.Trim();
                _currentPairCodeIssuedAtUtc = TryParseIssuedAt(_globalSettings.SyncApiPairCodeIssuedAtUtc);
            }

            int ttlMinutes = Math.Clamp(_globalSettings.SyncApiPairCodeTtlMinutes, 1, 1440);
            DateTimeOffset expiresAt = _currentPairCodeIssuedAtUtc.AddMinutes(ttlMinutes);
            return DateTimeOffset.UtcNow > expiresAt;
        }
    }

    private void RotatePairCode()
    {
        lock (_pairCodeSync)
        {
            _currentPairCode = AppGlobalSettings.GenerateSyncPairCode();
            _currentPairCodeIssuedAtUtc = DateTimeOffset.UtcNow;

            _globalSettings.SyncApiPairCode = _currentPairCode;
            _globalSettings.SyncApiPairCodeIssuedAtUtc = _currentPairCodeIssuedAtUtc.ToString("O");
        }
    }

    private void AppendPairingHistory(string deviceLabel, DateTimeOffset pairedAtUtc)
    {
        lock (_pairCodeSync)
        {
            List<PairingHistoryEntry> items;
            try
            {
                items = JsonSerializer.Deserialize<List<PairingHistoryEntry>>(
                    _globalSettings.SyncApiPairingHistoryJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
            catch
            {
                items = [];
            }

            items.Insert(0, new PairingHistoryEntry
            {
                DeviceLabel = string.IsNullOrWhiteSpace(deviceLabel) ? "Unknown device" : deviceLabel,
                PairedAtUtc = pairedAtUtc.ToString("O"),
            });

            while (items.Count > MaxPairingHistoryEntries)
            {
                items.RemoveAt(items.Count - 1);
            }

            _globalSettings.SyncApiPairingHistoryJson = JsonSerializer.Serialize(items);
        }
    }

    private static DateTimeOffset TryParseIssuedAt(string? value)
    {
        return DateTimeOffset.TryParse(value, out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
    }

    private static bool IsAuthorized(HttpListenerRequest request, string expectedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return true;
        }

        string? suppliedToken = request.Headers[SyncTokenHeader];
        if (string.IsNullOrWhiteSpace(suppliedToken))
        {
            string? authorization = request.Headers["Authorization"];
            if (!string.IsNullOrWhiteSpace(authorization)
                && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                suppliedToken = authorization["Bearer ".Length..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(suppliedToken))
        {
            return false;
        }

        return FixedTimeEquals(expectedToken, suppliedToken);
    }

    private static bool FixedTimeEquals(string expected, string supplied)
    {
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        byte[] suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private async Task ExecuteSerializedMutationAsync(
        Func<Task> mutation,
        HttpListenerRequest? request,
        HttpListenerResponse? response,
        CancellationToken cancellationToken)
    {
        if (!await _mutationGate.WaitAsync(MaxMutationWaitDuration, cancellationToken))
        {
            Interlocked.Increment(ref _busyMutationRejectionsTotal);
            Logger.LogWarning("SyncApi", "Sync API mutation gate timed out", new Dictionary<string, object?>
            {
                ["method"] = request?.HttpMethod,
                ["path"] = request?.Url?.AbsolutePath,
                ["waitMs"] = (long)MaxMutationWaitDuration.TotalMilliseconds,
            });

            if (response is not null)
            {
                await WriteErrorAsync(response, HttpStatusCode.ServiceUnavailable, "Sync API is busy; retry later");
            }

            return;
        }

        try
        {
            await mutation();
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private void LogRequestCompletion(HttpListenerRequest request, HttpListenerResponse response, TimeSpan elapsed)
    {
        Interlocked.Increment(ref _requestsTotal);

        if (response.StatusCode is >= 400 and <= 499)
        {
            Interlocked.Increment(ref _clientErrorsTotal);
        }
        else if (response.StatusCode >= 500)
        {
            Interlocked.Increment(ref _serverErrorsTotal);
        }

        var context = new Dictionary<string, object?>
        {
            ["method"] = request.HttpMethod,
            ["path"] = request.Url?.AbsolutePath,
            ["statusCode"] = response.StatusCode,
            ["elapsedMs"] = (long)elapsed.TotalMilliseconds,
        };

        if (elapsed >= SlowRequestThreshold)
        {
            Interlocked.Increment(ref _slowRequestsTotal);
            Logger.LogWarning("SyncApi", "Sync API request completed slowly", context);
            return;
        }

        Logger.LogInfo("SyncApi", "Sync API request completed", context);
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
                ["prefix"] = _activeListenerPrefix,
            });
        }

        _mutationGate.Dispose();
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
        public string? LibrarySource { get; set; }
    }

    private sealed class IngestionEventRequest
    {
        public string? FromState { get; set; }
        public string ToState { get; set; } = string.Empty;
        public string? Details { get; set; }
        public bool AllowFromStateOverride { get; set; }
    }

    private sealed class PairClaimRequest
    {
        public string PairCode { get; set; } = string.Empty;
        public string? DeviceLabel { get; set; }
    }

    private sealed class PairingHistoryEntry
    {
        [JsonPropertyName("deviceLabel")]
        public string DeviceLabel { get; set; } = string.Empty;

        [JsonPropertyName("pairedAtUtc")]
        public string PairedAtUtc { get; set; } = string.Empty;
    }
}
