using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Utilities;
using ChartHub.ViewModels;
using ChartHub.Tests.TestInfrastructure;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class SyncViewModelTests
{
    [Fact]
    public void ConnectionHint_WithoutToken_PromptsPairOrTokenFlow()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-hint-no-token");
        var settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient();

        var sut = new SyncViewModel(client, settings)
        {
            SyncToken = string.Empty,
        };

        Assert.True(sut.HasConnectionHint);
        Assert.Contains("Pair + Connect", sut.ConnectionHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnectionCommand_WithEmptyQueue_ShowsEmptyState()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-empty-state");
        var settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([]),
        };

        var sut = new SyncViewModel(client, settings);

        await sut.TestConnectionCommand.ExecuteAsync(null);

        Assert.True(sut.IsConnected);
        Assert.True(sut.ShowEmptyQueueState);
        Assert.False(sut.HasQueueItems);
        Assert.Equal("Connected. No ingestion items currently queued.", sut.StatusMessage);
    }

    [Fact]
    public async Task RetrySelectedCommand_RefreshesQueueAfterAction()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-retry-refresh");
        var settings = CreateSettings(temp.RootPath);

        var getIngestionsCalls = 0;
        var triggerRetryCalls = 0;

        var queueItem = new IngestionQueueItem
        {
            IngestionId = 42,
            Source = "googledrive",
            SourceLink = "https://drive.google.com/file/d/abc/view",
            DisplayName = "Song",
            CurrentState = IngestionState.Downloaded,
            DesktopState = DesktopState.Cloud,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) =>
            {
                getIngestionsCalls++;
                return Task.FromResult<IReadOnlyList<IngestionQueueItem>>([queueItem]);
            },
            TriggerRetryHandler = (_, _, _, _) =>
            {
                triggerRetryCalls++;
                return Task.CompletedTask;
            },
        };

        var sut = new SyncViewModel(client, settings);
        await sut.TestConnectionCommand.ExecuteAsync(null);
        await sut.RetrySelectedCommand.ExecuteAsync(queueItem);

        Assert.Equal(1, triggerRetryCalls);
        Assert.Equal(2, getIngestionsCalls);
        Assert.True(sut.HasQueueItems);
    }

    [Fact]
    public async Task TestConnectionCommand_OnFailure_ClearsQueueAndShowsGuidance()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-failure-guidance");
        var settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => throw new InvalidOperationException("invalid token"),
        };

        var sut = new SyncViewModel(client, settings);
        sut.QueueItems.Add(new IngestionQueueItem
        {
            IngestionId = 42,
            Source = "googledrive",
            SourceLink = "https://drive.google.com/file/d/abc/view",
            DisplayName = "Song",
            CurrentState = IngestionState.Downloaded,
            DesktopState = DesktopState.Cloud,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        await sut.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(sut.IsConnected);
        Assert.True(sut.HasError);
        Assert.Empty(sut.QueueItems);
        Assert.Equal("Connection failed. Verify desktop URL and credentials, then retry.", sut.StatusMessage);
        Assert.Equal("Connection failed. Verify desktop URL and credentials, then retry.", sut.ConnectionHint);
    }

    [Fact]
    public async Task RetrySelectedCommand_SetsSuccessResultOnCompletion()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-retry-result");
        var settings = CreateSettings(temp.RootPath);

        var queueItem = new IngestionQueueItem
        {
            IngestionId = 42,
            Source = "googledrive",
            SourceLink = "https://drive.google.com/file/d/abc/view",
            DisplayName = "Song",
            CurrentState = IngestionState.Downloaded,
            DesktopState = DesktopState.Cloud,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([queueItem]),
            TriggerRetryHandler = (_, _, _, _) => Task.CompletedTask,
        };

        var sut = new SyncViewModel(client, settings);
        await sut.TestConnectionCommand.ExecuteAsync(null);

        Assert.Null(queueItem.LastActionResult);

        await sut.RetrySelectedCommand.ExecuteAsync(queueItem);

        Assert.NotNull(queueItem.LastActionResult);
        Assert.Equal(ActionType.Retry, queueItem.LastActionResult.ActionType);
        Assert.Equal(ActionResultStatus.Success, queueItem.LastActionResult.Status);
        Assert.Contains("successfully", queueItem.LastActionResult.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(queueItem.HasActionResult);
    }

    [Fact]
    public async Task InstallSelectedCommand_SetsSuccessResultOnCompletion()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-install-result");
        var settings = CreateSettings(temp.RootPath);

        var queueItem = new IngestionQueueItem
        {
            IngestionId = 42,
            Source = "googledrive",
            SourceLink = "https://drive.google.com/file/d/abc/view",
            DisplayName = "Song",
            CurrentState = IngestionState.Downloaded,
            DesktopState = DesktopState.Cloud,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([queueItem]),
            TriggerInstallHandler = (_, _, _, _) => Task.CompletedTask,
        };

        var sut = new SyncViewModel(client, settings);
        await sut.TestConnectionCommand.ExecuteAsync(null);

        await sut.InstallSelectedCommand.ExecuteAsync(queueItem);

        Assert.NotNull(queueItem.LastActionResult);
        Assert.Equal(ActionType.Install, queueItem.LastActionResult.ActionType);
        Assert.Equal(ActionResultStatus.Success, queueItem.LastActionResult.Status);
    }

    [Fact]
    public async Task OpenFolderSelectedCommand_SetsSuccessResultOnCompletion()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-openfolder-result");
        var settings = CreateSettings(temp.RootPath);

        var queueItem = new IngestionQueueItem
        {
            IngestionId = 42,
            Source = "googledrive",
            SourceLink = "https://drive.google.com/file/d/abc/view",
            DisplayName = "Song",
            CurrentState = IngestionState.Downloaded,
            DesktopState = DesktopState.Cloud,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([queueItem]),
            TriggerOpenFolderHandler = (_, _, _, _) => Task.CompletedTask,
        };

        var sut = new SyncViewModel(client, settings);
        await sut.TestConnectionCommand.ExecuteAsync(null);

        await sut.OpenFolderSelectedCommand.ExecuteAsync(queueItem);

        Assert.NotNull(queueItem.LastActionResult);
        Assert.Equal(ActionType.OpenFolder, queueItem.LastActionResult.ActionType);
        Assert.Equal(ActionResultStatus.Success, queueItem.LastActionResult.Status);
    }

    [Fact]
    public async Task RetrySelectedCommand_SetsFailedResultOnException()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-retry-failure");
        var settings = CreateSettings(temp.RootPath);

        var queueItem = new IngestionQueueItem
        {
            IngestionId = 42,
            Source = "googledrive",
            SourceLink = "https://drive.google.com/file/d/abc/view",
            DisplayName = "Song",
            CurrentState = IngestionState.Downloaded,
            DesktopState = DesktopState.Cloud,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([queueItem]),
            TriggerRetryHandler = (_, _, _, _) => throw new InvalidOperationException("network error"),
        };

        var sut = new SyncViewModel(client, settings);
        await sut.TestConnectionCommand.ExecuteAsync(null);

        await sut.RetrySelectedCommand.ExecuteAsync(queueItem);

        Assert.NotNull(queueItem.LastActionResult);
        Assert.Equal(ActionResultStatus.Failed, queueItem.LastActionResult.Status);
        Assert.Contains("network error", queueItem.LastActionResult.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionResult_DisplayTextFormatsCorrectly()
    {
        var result = new ActionResult
        {
            ActionType = ActionType.Install,
            Status = ActionResultStatus.Success,
            Message = "Installation completed",
            InitiatedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(500),
        };

        Assert.Equal("Install completed", result.DisplayText);
        Assert.True(result.DurationMs > 0);
    }

    [Fact]
    public void ActionResult_StatusBadgeIndicatesStatus()
    {
        var pending = new ActionResult
        {
            ActionType = ActionType.Retry,
            Status = ActionResultStatus.Pending,
            Message = "Processing",
            InitiatedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        };

        var success = new ActionResult
        {
            ActionType = ActionType.Retry,
            Status = ActionResultStatus.Success,
            Message = "Success",
            InitiatedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        };

        var failed = new ActionResult
        {
            ActionType = ActionType.Retry,
            Status = ActionResultStatus.Failed,
            Message = "Failed",
            InitiatedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        };

        Assert.Equal("⏳", pending.StatusBadge);
        Assert.Equal("✓", success.StatusBadge);
        Assert.Equal("✗", failed.StatusBadge);
    }

    [Fact]
    public void IngestionQueueItem_TracksActionResult()
    {
        var item = new IngestionQueueItem
        {
            IngestionId = 1,
            Source = "test",
            DisplayName = "Test Item",
            SourceLink = "https://example.com",
            CurrentState = IngestionState.Downloaded,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        Assert.Null(item.LastActionResult);
        Assert.False(item.HasActionResult);

        var result = new ActionResult
        {
            ActionType = ActionType.Install,
            Status = ActionResultStatus.Success,
            Message = "Installed",
            InitiatedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        };

        item.LastActionResult = result;

        Assert.NotNull(item.LastActionResult);
        Assert.True(item.HasActionResult);
        Assert.Equal("Install completed", item.ActionResultDisplay);
    }

    [Fact]
    public async Task AutoRefresh_ConnectedState_PeriodicallyRefreshesQueue()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-auto-refresh");
        var settings = CreateSettings(temp.RootPath);

        var getIngestionsCalls = 0;
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) =>
            {
                Interlocked.Increment(ref getIngestionsCalls);
                return Task.FromResult<IReadOnlyList<IngestionQueueItem>>([]);
            },
        };

        using var sut = new SyncViewModel(client, settings, TimeSpan.FromMilliseconds(30));

        await sut.TestConnectionCommand.ExecuteAsync(null);

        var refreshed = await WaitForConditionAsync(() => Volatile.Read(ref getIngestionsCalls) >= 2, TimeSpan.FromSeconds(2));

        Assert.True(refreshed);
        Assert.True(sut.IsConnected);
        Assert.True(sut.IsAutoRefreshActive);
    }

    [Fact]
    public async Task AutoRefresh_RefreshFailure_DisconnectsAndStopsLoop()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-auto-refresh-failure");
        var settings = CreateSettings(temp.RootPath);

        var getIngestionsCalls = 0;
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) =>
            {
                var callNumber = Interlocked.Increment(ref getIngestionsCalls);
                if (callNumber == 1)
                    return Task.FromResult<IReadOnlyList<IngestionQueueItem>>([]);

                throw new InvalidOperationException("auto refresh failure");
            },
        };

        using var sut = new SyncViewModel(client, settings, TimeSpan.FromMilliseconds(30));

        await sut.TestConnectionCommand.ExecuteAsync(null);

        var disconnected = await WaitForConditionAsync(() => !sut.IsConnected, TimeSpan.FromSeconds(2));

        Assert.True(disconnected);
        Assert.False(sut.IsConnected);
        Assert.False(sut.IsAutoRefreshActive);
        Assert.True(sut.HasError);
        Assert.Contains("auto refresh failure", sut.ErrorMessage, StringComparison.Ordinal);
    }

    private static async Task<bool> WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            if (predicate())
                return true;

            await Task.Delay(20);
        }

        return predicate();
    }

    [Fact]
    public async Task TestConnectionCommand_OnSuccess_CapturesServerInfo()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-diagnostics-success");
        var settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([]),
        };

        using var sut = new SyncViewModel(client, settings);

        await sut.TestConnectionCommand.ExecuteAsync(null);

        Assert.True(sut.IsConnected);
        Assert.NotNull(sut.Diagnostics.ServerInfo);
        Assert.Contains("ingestion-sync", sut.Diagnostics.ServerInfo);
        Assert.Contains("1.0.0", sut.Diagnostics.ServerInfo);
        Assert.Null(sut.Diagnostics.LastErrorMessage);
        Assert.Equal("✓ Connected to ingestion-sync v1.0.0", sut.DiagnosticsSummary);
    }

    [Fact]
    public async Task TestConnectionCommand_OnNetworkFailure_ClassifiesNetworkError()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-diagnostics-network");
        var settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => throw new HttpRequestException("The host did server not found."),
        };

        using var sut = new SyncViewModel(client, settings);

        await sut.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(sut.IsConnected);
        Assert.Equal(ErrorCategory.NetworkUnreachable, sut.Diagnostics.LastErrorCategory);
        Assert.Contains("URL", sut.Diagnostics.RemediationHint);
        Assert.Contains("reachable", sut.Diagnostics.RemediationHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestConnectionCommand_OnAuthFailure_ClassifiesAuthError()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-diagnostics-auth");
        var settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => throw new UnauthorizedAccessException("Invalid token or authentication failed."),
        };

        using var sut = new SyncViewModel(client, settings);

        await sut.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(sut.IsConnected);
        Assert.Equal(ErrorCategory.AuthenticationFailed, sut.Diagnostics.LastErrorCategory);
        Assert.Contains("token", sut.Diagnostics.RemediationHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestConnectionCommand_OnUnsupportedVersion_ClassifiesVersionError()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-diagnostics-version");
        var settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("old-api", "0.9.0", false, false)),
        };

        using var sut = new SyncViewModel(client, settings);

        await sut.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(sut.IsConnected);
        Assert.Equal(ErrorCategory.UnsupportedVersion, sut.Diagnostics.LastErrorCategory);
        Assert.Contains("ingestion", sut.Diagnostics.LastErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConnectionDiagnostics_IsHealthyWhenConnected()
    {
        var diagnostics = new ConnectionDiagnostics
        {
            ServerInfo = "ingestion-sync v1.0.0",
            LastErrorMessage = null,
            LastErrorCategory = null,
            RemediationHint = null
        };

        Assert.True(diagnostics.IsHealthy);
        Assert.Contains("✓", diagnostics.DiagnosticsSummary);
        Assert.Contains("Connected", diagnostics.DiagnosticsSummary);
    }

    [Fact]
    public void ConnectionDiagnostics_ShowsErrorWhenFailed()
    {
        var diagnostics = new ConnectionDiagnostics
        {
            ServerInfo = null,
            LastErrorMessage = "Connection refused",
            LastErrorCategory = ErrorCategory.NetworkUnreachable,
            RemediationHint = "Check desktop host is running."
        };

        Assert.False(diagnostics.IsHealthy);
        Assert.Contains("✗", diagnostics.DiagnosticsSummary);
        Assert.Contains("unreachable", diagnostics.DiagnosticsSummary, StringComparison.OrdinalIgnoreCase);
    }

    private static AppGlobalSettings CreateSettings(string rootPath)
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
                SyncApiDesktopBaseUrl = "http://127.0.0.1:15123",
            },
        };

        return new AppGlobalSettings(new FakeSettingsOrchestrator(config));
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

    private sealed class StubDesktopSyncApiClient : IDesktopSyncApiClient
    {
        public Func<string, string, string?, CancellationToken, Task<DesktopSyncPairClaimResponse>>? ClaimPairTokenHandler { get; init; }
        public Func<string, string, CancellationToken, Task<DesktopSyncVersionResponse>>? GetVersionHandler { get; init; }
        public Func<string, string, int, CancellationToken, Task<IReadOnlyList<IngestionQueueItem>>>? GetIngestionsHandler { get; init; }
        public Func<string, string, long, CancellationToken, Task>? TriggerRetryHandler { get; init; }
        public Func<string, string, long, CancellationToken, Task>? TriggerInstallHandler { get; init; }
        public Func<string, string, long, CancellationToken, Task>? TriggerOpenFolderHandler { get; init; }

        public Task<DesktopSyncPairClaimResponse> ClaimPairTokenAsync(string baseUrl, string pairCode, string? deviceLabel = null, CancellationToken cancellationToken = default)
            => ClaimPairTokenHandler?.Invoke(baseUrl, pairCode, deviceLabel, cancellationToken)
                ?? Task.FromResult(new DesktopSyncPairClaimResponse(true, "token", baseUrl));

        public Task<DesktopSyncVersionResponse> GetVersionAsync(string baseUrl, string token, CancellationToken cancellationToken = default)
            => GetVersionHandler?.Invoke(baseUrl, token, cancellationToken)
                ?? Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true));

        public Task<IReadOnlyList<IngestionQueueItem>> GetIngestionsAsync(string baseUrl, string token, int limit = 100, CancellationToken cancellationToken = default)
            => GetIngestionsHandler?.Invoke(baseUrl, token, limit, cancellationToken)
                ?? Task.FromResult<IReadOnlyList<IngestionQueueItem>>([]);

        public Task TriggerRetryAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default)
            => TriggerRetryHandler?.Invoke(baseUrl, token, ingestionId, cancellationToken)
                ?? Task.CompletedTask;

        public Task TriggerInstallAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default)
            => TriggerInstallHandler?.Invoke(baseUrl, token, ingestionId, cancellationToken)
                ?? Task.CompletedTask;

        public Task TriggerOpenFolderAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default)
            => TriggerOpenFolderHandler?.Invoke(baseUrl, token, ingestionId, cancellationToken)
                ?? Task.CompletedTask;
    }
}
