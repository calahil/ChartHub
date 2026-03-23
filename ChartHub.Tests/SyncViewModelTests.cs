using System.Text.Json;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class SyncViewModelTests
{
    [Fact]
    public void ConnectionHint_WithoutPendingPairing_PromptsQrScan()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-hint-qr");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient();

        using var sut = new SyncViewModel(client, settings, isCompanionMode: true);

        Assert.True(sut.HasConnectionHint);
        Assert.Contains("Scan the desktop QR", sut.ConnectionHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScanBootstrapQrCommand_WithValidPayload_SetsPendingConfirmation()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-scan-pending");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        StubDesktopSyncApiClient client = new();
        StubQrCodeScannerService scanner = CreateScanner("http://192.168.1.99:15123", "PAIR-3141", "Studio Desktop");

        using var sut = new SyncViewModel(client, settings, scanner, isCompanionMode: true);

        await sut.ScanBootstrapQrCommand.ExecuteAsync(null);

        Assert.True(sut.HasPendingQrPairing);
        Assert.Contains("Studio Desktop", sut.PendingQrPairingSummary, StringComparison.Ordinal);
        Assert.Contains("192.168.1.99", sut.PendingQrPairingSummary, StringComparison.Ordinal);
        Assert.False(sut.IsConnected);
    }

    [Fact]
    public async Task CancelPendingQrPairingCommand_ClearsPendingState()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-cancel-pending");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        StubDesktopSyncApiClient client = new();
        StubQrCodeScannerService scanner = CreateScanner("http://192.168.1.99:15123", "PAIR-3141", "Studio Desktop");

        using var sut = new SyncViewModel(client, settings, scanner, isCompanionMode: true);

        await sut.ScanBootstrapQrCommand.ExecuteAsync(null);
        sut.CancelPendingQrPairingCommand.Execute(null);

        Assert.False(sut.HasPendingQrPairing);
        Assert.Contains("canceled", sut.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmQrPairingCommand_UsesClaimedApiBaseUrl_WhenItIsValid()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-confirm-valid-url");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        StubDesktopSyncApiClient client = new()
        {
            ClaimPairTokenHandler = (_, _, _, _) => Task.FromResult(new DesktopSyncPairClaimResponse(true, "token-claimed", "http://192.168.1.55:15123")),
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([]),
        };
        StubQrCodeScannerService scanner = CreateScanner("http://192.168.1.44:15123", "PAIR-1234", "Living Room Desktop");

        using var sut = new SyncViewModel(client, settings, scanner, isCompanionMode: true);

        await sut.ScanBootstrapQrCommand.ExecuteAsync(null);
        await sut.ConfirmQrPairingCommand.ExecuteAsync(null);

        Assert.Equal("http://192.168.1.55:15123", sut.DesktopApiBaseUrl);
        Assert.Equal("http://192.168.1.55:15123", settings.SyncApiDesktopBaseUrl);
        Assert.True(sut.IsConnected);
        Assert.False(sut.HasPendingQrPairing);
    }

    [Fact]
    public async Task ConfirmQrPairingCommand_DoesNotReplaceLanUrl_WithLoopbackClaimUrl()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-confirm-loopback");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        StubDesktopSyncApiClient client = new()
        {
            ClaimPairTokenHandler = (_, _, _, _) => Task.FromResult(new DesktopSyncPairClaimResponse(true, "token-claimed", "http://127.0.0.1:15123")),
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([]),
        };
        const string lanUrl = "http://192.168.1.44:15123";
        StubQrCodeScannerService scanner = CreateScanner(lanUrl, "PAIR-1234", "Living Room Desktop");

        using var sut = new SyncViewModel(client, settings, scanner, isCompanionMode: true);

        await sut.ScanBootstrapQrCommand.ExecuteAsync(null);
        await sut.ConfirmQrPairingCommand.ExecuteAsync(null);

        Assert.Equal(lanUrl, sut.DesktopApiBaseUrl);
        Assert.Equal(lanUrl, settings.SyncApiDesktopBaseUrl);
    }

    [Fact]
    public async Task ConfirmQrPairingCommand_LoadsQueueAfterPairing()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-confirm-loads-queue");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        IngestionQueueItem queueItem = CreateQueueItem();
        StubDesktopSyncApiClient client = new()
        {
            ClaimPairTokenHandler = (_, _, _, _) => Task.FromResult(new DesktopSyncPairClaimResponse(true, "token-claimed", "http://192.168.1.55:15123")),
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([queueItem]),
        };
        StubQrCodeScannerService scanner = CreateScanner("http://192.168.1.44:15123", "PAIR-1234", "Living Room Desktop");

        using var sut = new SyncViewModel(client, settings, scanner, isCompanionMode: true);

        await sut.ScanBootstrapQrCommand.ExecuteAsync(null);
        await sut.ConfirmQrPairingCommand.ExecuteAsync(null);

        Assert.True(sut.IsConnected);
        Assert.True(sut.HasQueueItems);
        Assert.Single(sut.QueueItems);
        Assert.Equal("token-claimed", settings.SyncApiAuthToken);
    }

    [Fact]
    public async Task ConfirmQrPairingCommand_PersistsCredentials_WhenConnectionCheckFails()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-confirm-persist-on-fail");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        StubDesktopSyncApiClient client = new()
        {
            ClaimPairTokenHandler = (_, _, _, _) => Task.FromResult(new DesktopSyncPairClaimResponse(true, "token-claimed", "http://192.168.1.55:15123")),
            GetVersionHandler = (_, _, _) => throw new InvalidOperationException("Simulated connection failure"),
        };
        StubQrCodeScannerService scanner = CreateScanner("http://192.168.1.44:15123", "PAIR-1234", "Living Room Desktop");

        using var sut = new SyncViewModel(client, settings, scanner, isCompanionMode: true);

        await sut.ScanBootstrapQrCommand.ExecuteAsync(null);
        await sut.ConfirmQrPairingCommand.ExecuteAsync(null);

        Assert.False(sut.IsConnected);
        Assert.Equal("token-claimed", settings.SyncApiAuthToken);
        Assert.Equal("http://192.168.1.55:15123", settings.SyncApiDesktopBaseUrl);
    }

    [Fact]
    public void GeneratedBootstrapPayload_UsesAdvertisedBaseUrlOverride()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-bootstrap-override");
        AppGlobalSettings settings = CreateSettings(
            temp.RootPath,
            syncApiListenPrefix: "http://localhost:15123/",
            syncApiAdvertisedBaseUrl: "http://192.168.1.55:15123");
        var client = new StubDesktopSyncApiClient();
        using var sut = new SyncViewModel(client, settings, isCompanionMode: false)
        {
            DesktopApiBaseUrl = "http://127.0.0.1:15123",
            PairCode = "PAIR-5555",
        };

        SyncBootstrapPayload payload = DecodeBootstrapPayload(sut.GeneratedBootstrapPayload);

        Assert.Equal("http://192.168.1.55:15123", payload.ApiBaseUrl);
        Assert.Equal("PAIR-5555", payload.PairCode);
        Assert.Equal("ChartHub Desktop", payload.DeviceLabel);
    }

    [Fact]
    public void GeneratedBootstrapPayload_WhenOnlyLoopbackAvailable_IsEmpty()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-bootstrap-loopback-only");
        AppGlobalSettings settings = CreateSettings(
            temp.RootPath,
            syncApiListenPrefix: "http://localhost:15123/");
        var client = new StubDesktopSyncApiClient();
        using var sut = new SyncViewModel(client, settings, isCompanionMode: false)
        {
            PairCode = "PAIR-7788",
        };

        Assert.Equal(string.Empty, sut.GeneratedBootstrapPayload);
        Assert.False(sut.HasBootstrapQrImage);
    }

    [Fact]
    public void Constructor_InDesktopMode_ShowsDesktopBootstrapSection()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-desktop-layout");
        AppGlobalSettings settings = CreateSettings(temp.RootPath, syncApiAdvertisedBaseUrl: "http://192.168.1.55:15123");
        var client = new StubDesktopSyncApiClient();

        using var sut = new SyncViewModel(client, settings, isCompanionMode: false);
        sut.PairCode = "PAIR-1234";

        Assert.True(sut.IsDesktopMode);
        Assert.False(sut.IsCompanionMode);
        Assert.True(sut.ShowDesktopBootstrapSection);
        Assert.Equal("Desktop Sync Host", sut.SyncTitle);
        Assert.Contains("QR", sut.BootstrapInstructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_InCompanionMode_ShowsScanSectionUntilConnected()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-companion-layout");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient();

        using var sut = new SyncViewModel(client, settings, isCompanionMode: true);

        Assert.True(sut.IsCompanionMode);
        Assert.False(sut.IsDesktopMode);
        Assert.False(sut.ShowDesktopBootstrapSection);
        Assert.True(sut.ShowCompanionScanSection);
        Assert.False(sut.ShowCompanionConfirmationSection);
        Assert.False(sut.ShowCompanionQueueSection);
    }

    [Fact]
    public async Task TestConnectionCommand_WithEmptyQueue_ShowsEmptyState()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-empty-state");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([]),
        };

        using var sut = new SyncViewModel(client, settings, isCompanionMode: true);
        sut.SyncToken = "persisted-token";
        sut.DesktopApiBaseUrl = "http://192.168.1.55:15123";

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
        AppGlobalSettings settings = CreateSettings(temp.RootPath);

        int getIngestionsCalls = 0;
        int triggerRetryCalls = 0;
        IngestionQueueItem queueItem = CreateQueueItem();

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

        using var sut = new SyncViewModel(client, settings, isCompanionMode: true);
        sut.SyncToken = "persisted-token";
        sut.DesktopApiBaseUrl = "http://192.168.1.55:15123";

        await sut.TestConnectionCommand.ExecuteAsync(null);
        await sut.RetrySelectedCommand.ExecuteAsync(queueItem);

        Assert.Equal(1, triggerRetryCalls);
        Assert.Equal(2, getIngestionsCalls);
        Assert.True(sut.HasQueueItems);
    }

    [Fact]
    public async Task TestConnectionCommand_OnFailure_ClearsQueueAndShowsQrGuidance()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-failure-guidance");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => throw new InvalidOperationException("invalid token"),
        };

        using var sut = new SyncViewModel(client, settings, isCompanionMode: true);
        sut.SyncToken = "persisted-token";
        sut.DesktopApiBaseUrl = "http://192.168.1.55:15123";
        sut.QueueItems.Add(CreateQueueItem());

        await sut.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(sut.IsConnected);
        Assert.True(sut.HasError);
        Assert.Empty(sut.QueueItems);
        Assert.Equal("Pairing failed. Scan the desktop QR and try again.", sut.StatusMessage);
        Assert.Contains("Scan the desktop QR", sut.ConnectionHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetrySelectedCommand_SetsSuccessResultOnCompletion()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-retry-result");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        IngestionQueueItem queueItem = CreateQueueItem();

        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([queueItem]),
            TriggerRetryHandler = (_, _, _, _) => Task.CompletedTask,
        };

        using var sut = new SyncViewModel(client, settings, isCompanionMode: true);
        sut.SyncToken = "persisted-token";
        sut.DesktopApiBaseUrl = "http://192.168.1.55:15123";

        await sut.TestConnectionCommand.ExecuteAsync(null);
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
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        IngestionQueueItem queueItem = CreateQueueItem();

        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([queueItem]),
            TriggerInstallHandler = (_, _, _, _) => Task.CompletedTask,
        };

        using var sut = new SyncViewModel(client, settings, isCompanionMode: true);
        sut.SyncToken = "persisted-token";
        sut.DesktopApiBaseUrl = "http://192.168.1.55:15123";

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
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        IngestionQueueItem queueItem = CreateQueueItem();

        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([queueItem]),
            TriggerOpenFolderHandler = (_, _, _, _) => Task.CompletedTask,
        };

        using var sut = new SyncViewModel(client, settings, isCompanionMode: true);
        sut.SyncToken = "persisted-token";
        sut.DesktopApiBaseUrl = "http://192.168.1.55:15123";

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
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        IngestionQueueItem queueItem = CreateQueueItem();

        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([queueItem]),
            TriggerRetryHandler = (_, _, _, _) => throw new InvalidOperationException("network error"),
        };

        using var sut = new SyncViewModel(client, settings, isCompanionMode: true);
        sut.SyncToken = "persisted-token";
        sut.DesktopApiBaseUrl = "http://192.168.1.55:15123";

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
        AppGlobalSettings settings = CreateSettings(temp.RootPath);

        int getIngestionsCalls = 0;
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) =>
            {
                Interlocked.Increment(ref getIngestionsCalls);
                return Task.FromResult<IReadOnlyList<IngestionQueueItem>>([]);
            },
        };

        using var sut = new SyncViewModel(client, settings, autoRefreshInterval: TimeSpan.FromMilliseconds(30), isCompanionMode: true);
        sut.SyncToken = "persisted-token";
        sut.DesktopApiBaseUrl = "http://192.168.1.55:15123";

        await sut.TestConnectionCommand.ExecuteAsync(null);

        bool refreshed = await WaitForConditionAsync(() => Volatile.Read(ref getIngestionsCalls) >= 2, TimeSpan.FromSeconds(2));

        Assert.True(refreshed);
        Assert.True(sut.IsConnected);
        Assert.True(sut.IsAutoRefreshActive);
    }

    [Fact]
    public async Task AutoRefresh_RefreshFailure_DisconnectsAndStopsLoop()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-auto-refresh-failure");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);

        int getIngestionsCalls = 0;
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) =>
            {
                int callNumber = Interlocked.Increment(ref getIngestionsCalls);
                if (callNumber == 1)
                {
                    return Task.FromResult<IReadOnlyList<IngestionQueueItem>>([]);
                }

                throw new InvalidOperationException("auto refresh failure");
            },
        };

        using var sut = new SyncViewModel(client, settings, autoRefreshInterval: TimeSpan.FromMilliseconds(30), isCompanionMode: true);
        sut.SyncToken = "persisted-token";
        sut.DesktopApiBaseUrl = "http://192.168.1.55:15123";

        await sut.TestConnectionCommand.ExecuteAsync(null);

        bool disconnected = await WaitForConditionAsync(() => !sut.IsConnected, TimeSpan.FromSeconds(2));

        Assert.True(disconnected);
        Assert.False(sut.IsConnected);
        Assert.False(sut.IsAutoRefreshActive);
        Assert.True(sut.HasError);
        Assert.Contains("auto refresh failure", sut.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnectionCommand_OnSuccess_CapturesServerInfo()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-diagnostics-success");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true)),
            GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([]),
        };

        using var sut = new SyncViewModel(client, settings, isCompanionMode: true);
        sut.SyncToken = "persisted-token";
        sut.DesktopApiBaseUrl = "http://192.168.1.55:15123";

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
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => throw new HttpRequestException("The host did server not found."),
        };

        using var sut = new SyncViewModel(client, settings, isCompanionMode: true);
        sut.SyncToken = "persisted-token";
        sut.DesktopApiBaseUrl = "http://192.168.1.55:15123";

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
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => throw new UnauthorizedAccessException("Invalid token or authentication failed."),
        };

        using var sut = new SyncViewModel(client, settings, isCompanionMode: true);
        sut.SyncToken = "persisted-token";
        sut.DesktopApiBaseUrl = "http://192.168.1.55:15123";

        await sut.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(sut.IsConnected);
        Assert.Equal(ErrorCategory.AuthenticationFailed, sut.Diagnostics.LastErrorCategory);
        Assert.Contains("token", sut.Diagnostics.RemediationHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestConnectionCommand_OnUnsupportedVersion_ClassifiesVersionError()
    {
        using var temp = new TemporaryDirectoryFixture("sync-vm-diagnostics-version");
        AppGlobalSettings settings = CreateSettings(temp.RootPath);
        var client = new StubDesktopSyncApiClient
        {
            GetVersionHandler = (_, _, _) => Task.FromResult(new DesktopSyncVersionResponse("old-api", "0.9.0", false, false)),
        };

        using var sut = new SyncViewModel(client, settings, isCompanionMode: true);
        sut.SyncToken = "persisted-token";
        sut.DesktopApiBaseUrl = "http://192.168.1.55:15123";

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

    private static async Task<bool> WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTime startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return predicate();
    }

    private static IngestionQueueItem CreateQueueItem()
    {
        return new IngestionQueueItem
        {
            IngestionId = 42,
            Source = "googledrive",
            SourceLink = "https://drive.google.com/file/d/abc/view",
            DisplayName = "Song",
            CurrentState = IngestionState.Downloaded,
            DesktopState = DesktopState.Cloud,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static AppGlobalSettings CreateSettings(
        string rootPath,
        string syncApiListenPrefix = "http://127.0.0.1:15123/",
        string syncApiAdvertisedBaseUrl = "")
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
                SyncApiListenPrefix = syncApiListenPrefix,
                SyncApiAdvertisedBaseUrl = syncApiAdvertisedBaseUrl,
            },
        };

        return new AppGlobalSettings(new FakeSettingsOrchestrator(config));
    }

    private static StubQrCodeScannerService CreateScanner(string baseUrl, string pairCode, string desktopLabel)
    {
        string payload = BuildBootstrapPayloadString(baseUrl, pairCode, desktopLabel);
        return new StubQrCodeScannerService
        {
            IsSupportedValue = true,
            ScanHandler = _ => Task.FromResult<string?>(payload),
        };
    }

    private static string BuildBootstrapPayloadString(string baseUrl, string pairCode, string desktopLabel)
    {
        var payload = new SyncBootstrapPayload(baseUrl, pairCode, desktopLabel, 1);
        string json = JsonSerializer.Serialize(payload);
        string encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"charthub-sync://bootstrap?d={encoded}";
    }

    private static SyncBootstrapPayload DecodeBootstrapPayload(string generatedPayload)
    {
        Assert.False(string.IsNullOrWhiteSpace(generatedPayload));
        Assert.True(Uri.TryCreate(generatedPayload, UriKind.Absolute, out Uri? payloadUri));
        Assert.NotNull(payloadUri);

        string? encodedPayload = ParseQueryValue(payloadUri.Query, "d");
        Assert.False(string.IsNullOrWhiteSpace(encodedPayload));

        string padded = encodedPayload!
            .Replace('-', '+')
            .Replace('_', '/');
        int mod4 = padded.Length % 4;
        if (mod4 > 0)
        {
            padded = padded.PadRight(padded.Length + (4 - mod4), '=');
        }

        string payloadJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        SyncBootstrapPayload? payload = JsonSerializer.Deserialize<SyncBootstrapPayload>(payloadJson);
        Assert.NotNull(payload);
        return payload!;
    }

    private static string? ParseQueryValue(string query, string key)
    {
        string trimmed = query.TrimStart('?');
        foreach (string segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = segment.Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            string currentKey = Uri.UnescapeDataString(parts[0]);
            if (!currentKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.UnescapeDataString(parts[1]);
        }

        return null;
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

    private sealed class StubQrCodeScannerService : IQrCodeScannerService
    {
        public bool IsSupportedValue { get; init; }
        public Func<CancellationToken, Task<string?>>? ScanHandler { get; init; }

        public bool IsSupported => IsSupportedValue;

        public Task<string?> ScanAsync(CancellationToken cancellationToken = default)
            => ScanHandler?.Invoke(cancellationToken) ?? Task.FromResult<string?>(null);
    }
}
