using System.Net;
using System.Runtime.CompilerServices;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Localization;
using ChartHub.Services;
using ChartHub.Strings;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public sealed class DesktopEntryViewModelTests
{
    [Fact]
    public async Task InitializeAsync_WithServerConnection_LoadsEntriesAndResolvesIconUrls()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-init");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, "http://127.0.0.1:5001", "token");

        var fakeClient = new FakeChartHubServerApiClient
        {
            Entries =
            [
                new ChartHubServerDesktopEntryResponse("z-id", "Zulu", "Stopped", null, "/icons/z.png"),
                new ChartHubServerDesktopEntryResponse("a-id", "Alpha", "Running", 42, "https://cdn.example/icon.png"),
            ],
        };

        var sut = new DesktopEntryViewModel(settings, fakeClient, uiInvoke: InvokeInline);
        try
        {
            bool loaded = await WaitForConditionAsync(() => sut.Entries.Count == 2, TimeSpan.FromSeconds(2));
            Assert.True(loaded);

            Assert.Equal("Alpha", sut.Entries[0].Name);
            Assert.Equal("Zulu", sut.Entries[1].Name);
            Assert.Equal("https://cdn.example/icon.png", sut.Entries[0].IconUrl);
            Assert.Equal("http://127.0.0.1:5001/icons/z.png", sut.Entries[1].IconUrl);
            Assert.Equal(UiLocalization.Get("DesktopEntry.ListRefreshed"), sut.StatusMessage);
            Assert.True(fakeClient.RefreshCallCount >= 1);
            Assert.True(fakeClient.ListCallCount >= 1);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public async Task InitializeAsync_WithoutServerConnection_ShowsConfigureLoadMessage()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-missing-settings");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, string.Empty, string.Empty);

        var sut = new DesktopEntryViewModel(settings, new FakeChartHubServerApiClient(), uiInvoke: InvokeInline);
        try
        {
            bool configured = await WaitForConditionAsync(
                () => string.Equals(sut.StatusMessage, UiLocalization.Get("DesktopEntry.ConfigureLoad"), StringComparison.Ordinal),
                TimeSpan.FromSeconds(2));

            Assert.True(configured);
            Assert.Empty(sut.Entries);
            Assert.False(sut.IsBusy);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public async Task ExecuteCommand_OnConflict_ShowsAlreadyRunningMessage()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-execute-conflict");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, "http://127.0.0.1:5001", "token");

        var fakeClient = new FakeChartHubServerApiClient
        {
            Entries = [new ChartHubServerDesktopEntryResponse("entry-1", "App", "Stopped", null, null)],
            ExecuteException = new ChartHubServerApiException(HttpStatusCode.Conflict, "Conflict", "already running", "conflict"),
        };

        var sut = new DesktopEntryViewModel(settings, fakeClient, uiInvoke: InvokeInline);
        try
        {
            bool loaded = await WaitForConditionAsync(() => sut.Entries.Count == 1, TimeSpan.FromSeconds(2));
            Assert.True(loaded);

            await sut.ExecuteCommand.ExecuteAsync(sut.Entries[0]);

            Assert.Equal(UiLocalization.Get("DesktopEntry.AlreadyRunning"), sut.StatusMessage);
            Assert.False(sut.IsBusy);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public async Task KillCommand_OnConflict_ShowsStillRunningMessage()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-kill-conflict");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, "http://127.0.0.1:5001", "token");

        var fakeClient = new FakeChartHubServerApiClient
        {
            Entries = [new ChartHubServerDesktopEntryResponse("entry-1", "App", "Running", 100, null)],
            KillException = new ChartHubServerApiException(HttpStatusCode.Conflict, "Conflict", "still running", "conflict"),
        };

        var sut = new DesktopEntryViewModel(settings, fakeClient, uiInvoke: InvokeInline);
        try
        {
            bool loaded = await WaitForConditionAsync(() => sut.Entries.Count == 1, TimeSpan.FromSeconds(2));
            Assert.True(loaded);

            await sut.KillCommand.ExecuteAsync(sut.Entries[0]);

            Assert.Equal(UiLocalization.Get("DesktopEntry.StillRunningAfterSigTerm"), sut.StatusMessage);
            Assert.False(sut.IsBusy);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public async Task ExecuteCommand_OnSuccess_UpdatesEntryStateAndStatusMessage()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-execute-success");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, "http://127.0.0.1:5001", "token");

        var fakeClient = new FakeChartHubServerApiClient
        {
            Entries = [new ChartHubServerDesktopEntryResponse("entry-1", "App", "Stopped", null, null)],
            ExecuteResponse = new ChartHubServerDesktopEntryActionResponse("entry-1", "Running", 777, "Started"),
        };

        var sut = new DesktopEntryViewModel(settings, fakeClient, uiInvoke: InvokeInline);
        try
        {
            bool loaded = await WaitForConditionAsync(() => sut.Entries.Count == 1, TimeSpan.FromSeconds(2));
            Assert.True(loaded);

            await sut.ExecuteCommand.ExecuteAsync(sut.Entries[0]);

            Assert.Equal("Running", sut.Entries[0].Status);
            Assert.Equal(777, sut.Entries[0].ProcessId);
            Assert.Equal("Started", sut.StatusMessage);
            Assert.False(sut.IsBusy);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public async Task KillCommand_OnSuccess_UpdatesEntryStateAndStatusMessage()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-kill-success");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, "http://127.0.0.1:5001", "token");

        var fakeClient = new FakeChartHubServerApiClient
        {
            Entries = [new ChartHubServerDesktopEntryResponse("entry-1", "App", "Running", 777, null)],
            KillResponse = new ChartHubServerDesktopEntryActionResponse("entry-1", "Stopped", null, "Stopped"),
        };

        var sut = new DesktopEntryViewModel(settings, fakeClient, uiInvoke: InvokeInline);
        try
        {
            bool loaded = await WaitForConditionAsync(() => sut.Entries.Count == 1, TimeSpan.FromSeconds(2));
            Assert.True(loaded);

            await sut.KillCommand.ExecuteAsync(sut.Entries[0]);

            Assert.Equal("Stopped", sut.Entries[0].Status);
            Assert.Null(sut.Entries[0].ProcessId);
            Assert.Equal("Stopped", sut.StatusMessage);
            Assert.False(sut.IsBusy);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public async Task StreamUpdatesAsync_AppliesIncomingSnapshotAndStatusMessage()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-stream");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, "http://127.0.0.1:5001", "token");

        var fakeClient = new FakeChartHubServerApiClient
        {
            Entries = [new ChartHubServerDesktopEntryResponse("entry-1", "App", "Stopped", null, null)],
            StreamBatches =
            [
                [new ChartHubServerDesktopEntryResponse("entry-1", "App", "Running", 345, "/icons/app.png")],
            ],
        };

        var sut = new DesktopEntryViewModel(settings, fakeClient, uiInvoke: InvokeInline);
        try
        {
            bool updated = await WaitForConditionAsync(
                () => sut.Entries.Count == 1
                      && string.Equals(sut.Entries[0].Status, "Running", StringComparison.Ordinal)
                      && sut.Entries[0].ProcessId == 345,
                TimeSpan.FromSeconds(2));

            Assert.True(updated);
            Assert.Equal(UiLocalization.Get("DesktopEntry.StatusUpdated"), sut.StatusMessage);
            Assert.Equal("http://127.0.0.1:5001/icons/app.png", sut.Entries[0].IconUrl);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public async Task RefreshCommand_WhenListFails_ShowsRefreshFailedMessageAndClearsBusy()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-refresh-failed");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, "http://127.0.0.1:5001", "token");

        var fakeClient = new FakeChartHubServerApiClient
        {
            ListException = new InvalidOperationException("boom"),
        };

        var sut = new DesktopEntryViewModel(settings, fakeClient, uiInvoke: InvokeInline);
        try
        {
            bool failed = await WaitForConditionAsync(
                () => sut.StatusMessage.Contains("boom", StringComparison.Ordinal),
                TimeSpan.FromSeconds(2));

            Assert.True(failed);
            Assert.False(sut.IsBusy);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public async Task ExecuteCommand_WithNullEntry_DoesNothing()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-execute-null");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, "http://127.0.0.1:5001", "token");

        var sut = new DesktopEntryViewModel(settings, new FakeChartHubServerApiClient(), uiInvoke: InvokeInline);
        try
        {
            await sut.ExecuteCommand.ExecuteAsync(null);
            Assert.False(sut.IsBusy);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public async Task ExecuteCommand_WithoutConnection_ShowsConfigureExecuteMessage()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-execute-no-connection");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, string.Empty, string.Empty);

        var sut = new DesktopEntryViewModel(settings, new FakeChartHubServerApiClient(), uiInvoke: InvokeInline);
        try
        {
            var entry = new DesktopEntryCardItem("entry-1", "App", "Stopped", null, null);

            await sut.ExecuteCommand.ExecuteAsync(entry);

            Assert.Equal(UiLocalization.Get("DesktopEntry.ConfigureExecute"), sut.StatusMessage);
            Assert.False(sut.IsBusy);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public async Task KillCommand_WithNullEntry_DoesNothing()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-kill-null");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, "http://127.0.0.1:5001", "token");

        var sut = new DesktopEntryViewModel(settings, new FakeChartHubServerApiClient(), uiInvoke: InvokeInline);
        try
        {
            await sut.KillCommand.ExecuteAsync(null);
            Assert.False(sut.IsBusy);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public async Task KillCommand_WithoutConnection_ShowsConfigureStopMessage()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-kill-no-connection");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, string.Empty, string.Empty);

        var sut = new DesktopEntryViewModel(settings, new FakeChartHubServerApiClient(), uiInvoke: InvokeInline);
        try
        {
            var entry = new DesktopEntryCardItem("entry-1", "App", "Running", 10, null);

            await sut.KillCommand.ExecuteAsync(entry);

            Assert.Equal(UiLocalization.Get("DesktopEntry.ConfigureStop"), sut.StatusMessage);
            Assert.False(sut.IsBusy);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public async Task DesktopModeAndEntryFlags_AreReportedFromCurrentState()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-flag-read");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, "http://127.0.0.1:5001", "token");

        var fakeClient = new FakeChartHubServerApiClient
        {
            Entries = [new ChartHubServerDesktopEntryResponse("entry-1", "App", "Stopped", null, null)],
        };

        var sut = new DesktopEntryViewModel(settings, fakeClient, uiInvoke: InvokeInline);
        try
        {
            bool loaded = await WaitForConditionAsync(() => sut.Entries.Count == 1, TimeSpan.FromSeconds(2));
            Assert.True(loaded);

            Assert.True(sut.IsDesktopMode);
            Assert.False(sut.IsCompanionMode);
            Assert.True(sut.HasEntries);
            Assert.NotNull(sut.RefreshCommand);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public async Task InitializeAsync_WithInvalidBaseUrl_LeavesRelativeIconUnset()
    {
        using var temp = new TemporaryDirectoryFixture("desktop-entry-invalid-base-url");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath, "not-a-valid-url", "token");

        var fakeClient = new FakeChartHubServerApiClient
        {
            Entries = [new ChartHubServerDesktopEntryResponse("entry-1", "App", "Stopped", null, "/icons/app.png")],
        };

        var sut = new DesktopEntryViewModel(settings, fakeClient, uiInvoke: InvokeInline);
        try
        {
            bool loaded = await WaitForConditionAsync(() => sut.Entries.Count == 1, TimeSpan.FromSeconds(2));
            Assert.True(loaded);
            Assert.Null(sut.Entries[0].IconUrl);
        }
        finally
        {
            sut.Dispose();
        }
    }

    [Fact]
    public void DesktopEntryCardItem_ApplyRunningState_UpdatesFlagsAndPid()
    {
        DesktopEntryCardItem item = new("retro", "RetroArch", "Not running", null, "https://example.test/icon.png");

        item.Apply("Running", 4242);

        Assert.True(item.IsRunning);
        Assert.True(item.CanKill);
        Assert.False(item.CanExecute);
        Assert.Equal("PID: 4242", item.PidLabel);
    }

    [Fact]
    public void DesktopEntryCardItem_ApplyStoppedState_UpdatesFlagsAndPid()
    {
        DesktopEntryCardItem item = new("retro", "RetroArch", "Running", 4242, "https://example.test/icon.png");

        item.Apply("Not running", null);

        Assert.False(item.IsRunning);
        Assert.False(item.CanKill);
        Assert.True(item.CanExecute);
        Assert.Equal("PID: -", item.PidLabel);
    }

    private static AppGlobalSettings CreateSettings(string rootPath, string serverApiBaseUrl, string serverApiAuthToken)
    {
        var orchestrator = new FakeSettingsOrchestrator(new AppConfigRoot
        {
            Runtime = new RuntimeAppConfig
            {
                ServerApiBaseUrl = serverApiBaseUrl,
                ServerApiAuthToken = serverApiAuthToken,
            },
        });

        return new AppGlobalSettings(orchestrator);
    }

    private static Task InvokeInline(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static async Task<bool> WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return predicate();
    }

    private sealed class FakeSettingsOrchestrator : ISettingsOrchestrator
    {
        public FakeSettingsOrchestrator(AppConfigRoot current)
        {
            Current = current;
        }

        public AppConfigRoot Current { get; private set; }

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

    private sealed class FakeChartHubServerApiClient : IChartHubServerApiClient
    {
        public IReadOnlyList<ChartHubServerDesktopEntryResponse> Entries { get; set; } = [];
        public IReadOnlyList<IReadOnlyList<ChartHubServerDesktopEntryResponse>> StreamBatches { get; set; } = [];
        public Exception? ExecuteException { get; set; }
        public Exception? KillException { get; set; }
        public Exception? ListException { get; set; }
        public ChartHubServerDesktopEntryActionResponse? ExecuteResponse { get; set; }
        public ChartHubServerDesktopEntryActionResponse? KillResponse { get; set; }
        public int RefreshCallCount { get; private set; }
        public int ListCallCount { get; private set; }

        public Task<ChartHubServerAuthExchangeResponse> ExchangeGoogleTokenAsync(string baseUrl, string googleIdToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ChartHubServerDownloadJobResponse> CreateDownloadJobAsync(string baseUrl, string bearerToken, ChartHubServerCreateDownloadJobRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ChartHubServerDownloadJobResponse>> ListDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<IReadOnlyList<ChartHubServerDownloadProgressEvent>> StreamDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ChartHubServerDownloadJobResponse> RequestInstallDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RequestCancelDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ChartHubServerDesktopEntryResponse>> ListDesktopEntriesAsync(
            string baseUrl,
            string bearerToken,
            CancellationToken cancellationToken = default)
        {
            ListCallCount++;
            if (ListException is not null)
            {
                throw ListException;
            }

            return Task.FromResult(Entries);
        }

        public Task<ChartHubServerDesktopEntryActionResponse> ExecuteDesktopEntryAsync(
            string baseUrl,
            string bearerToken,
            string entryId,
            CancellationToken cancellationToken = default)
        {
            if (ExecuteException is not null)
            {
                throw ExecuteException;
            }

            return Task.FromResult(ExecuteResponse ?? new ChartHubServerDesktopEntryActionResponse(entryId, "Running", 123, "started"));
        }

        public Task<ChartHubServerDesktopEntryActionResponse> KillDesktopEntryAsync(
            string baseUrl,
            string bearerToken,
            string entryId,
            CancellationToken cancellationToken = default)
        {
            if (KillException is not null)
            {
                throw KillException;
            }

            return Task.FromResult(KillResponse ?? new ChartHubServerDesktopEntryActionResponse(entryId, "Stopped", null, "stopped"));
        }

        public Task RefreshDesktopEntryCatalogAsync(
            string baseUrl,
            string bearerToken,
            CancellationToken cancellationToken = default)
        {
            RefreshCallCount++;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<IReadOnlyList<ChartHubServerDesktopEntryResponse>> StreamDesktopEntriesAsync(
            string baseUrl,
            string bearerToken,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (IReadOnlyList<ChartHubServerDesktopEntryResponse> batch in StreamBatches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return batch;
                await Task.Yield();
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
