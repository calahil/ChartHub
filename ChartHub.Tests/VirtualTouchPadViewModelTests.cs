using System.Text.Json;
using System.Text.Json.Nodes;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class VirtualTouchPadViewModelTests
{
    [Fact]
    public void Constructor_InitialisesDisconnectedStatusAndCommands()
    {
        using VirtualTouchPadViewModel sut = new(null, new FakeInputWebSocketService(), new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        Assert.False(sut.IsConnected);
        Assert.NotEmpty(sut.StatusMessage);
        Assert.NotNull(sut.PressMouseButtonCommand);
        Assert.NotNull(sut.ReleaseMouseButtonCommand);
        Assert.NotNull(sut.ActivateCommand);
        Assert.NotNull(sut.DeactivateCommand);
    }

    [Fact]
    public void Activate_RequestsLandscapeOrientation()
    {
        FakeOrientationService orientation = new();
        using VirtualTouchPadViewModel sut = new(null, new FakeInputWebSocketService(), orientation, new FakeDeviceDisplayNameProvider());

        sut.Activate();

        Assert.Equal(1, orientation.RequestLandscapeCallCount);
    }

    [Fact]
    public void Deactivate_SetsIsConnectedFalseAndRestoresOrientation()
    {
        FakeOrientationService orientation = new();
        using VirtualTouchPadViewModel sut = new(null, new FakeInputWebSocketService(), orientation, new FakeDeviceDisplayNameProvider());

        sut.Deactivate();

        Assert.False(sut.IsConnected);
        Assert.Equal(1, orientation.RestoreDefaultCallCount);
    }

    [Fact]
    public void OnTouchPadDelta_WhenNotConnected_DoesNotSendToWebSocket()
    {
        FakeInputWebSocketService ws = new();
        using VirtualTouchPadViewModel sut = new(null, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        sut.OnTouchPadDelta(10, 5);

        Assert.Empty(ws.SentMessages);
    }

    [Fact]
    public void OnTouchPadDelta_WhenConnected_SendsScaledMoveMessage()
    {
        FakeInputWebSocketService ws = new();
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token");
        using VirtualTouchPadViewModel sut = new(settings, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider("Pixel 8"));

        sut.Activate();
        bool connected = SpinWait.SpinUntil(() => sut.IsConnected, TimeSpan.FromSeconds(5));
        Assert.True(connected);
        Assert.Equal("Pixel 8", ws.LastDeviceName);

        ws.SentMessages.Clear();
        sut.OnTouchPadDelta(2, 3);

        // Default multiplier is 4.0: scaledDx = round(2*4.0)=8, scaledDy = round(3*4.0)=12.
        Assert.Single(ws.SentMessages);
        Assert.Contains("\"dx\":8", ws.SentMessages[0]);
        Assert.Contains("\"dy\":12", ws.SentMessages[0]);
    }

    [Fact]
    public void OnTouchPadDelta_WhenBothDeltasRoundToZero_DoesNotSend()
    {
        FakeInputWebSocketService ws = new();
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token");
        using VirtualTouchPadViewModel sut = new(settings, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        sut.Activate();
        bool connected = SpinWait.SpinUntil(() => sut.IsConnected, TimeSpan.FromSeconds(5));
        Assert.True(connected);

        ws.SentMessages.Clear();
        sut.OnTouchPadDelta(0, 0);

        Assert.Empty(ws.SentMessages);
    }

    [Fact]
    public void OnTouchPadDelta_UsesMouseSpeedMultiplierFromSettings()
    {
        FakeInputWebSocketService ws = new();
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token", mouseSpeedMultiplier: 2.0);
        using VirtualTouchPadViewModel sut = new(settings, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        sut.Activate();
        bool connected = SpinWait.SpinUntil(() => sut.IsConnected, TimeSpan.FromSeconds(5));
        Assert.True(connected);

        ws.SentMessages.Clear();
        sut.OnTouchPadDelta(5, 4);

        // Multiplier 2.0: scaledDx = round(5*2.0)=10, scaledDy = round(4*2.0)=8.
        Assert.Single(ws.SentMessages);
        Assert.Contains("\"dx\":10", ws.SentMessages[0]);
        Assert.Contains("\"dy\":8", ws.SentMessages[0]);
    }

    [Fact]
    public void PressMouseButtonCommand_WhenConnected_SendsMouseButtonPressMessage()
    {
        FakeInputWebSocketService ws = new();
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token");
        using VirtualTouchPadViewModel sut = new(settings, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        sut.Activate();
        bool connected = SpinWait.SpinUntil(() => sut.IsConnected, TimeSpan.FromSeconds(5));
        Assert.True(connected);

        ws.SentMessages.Clear();
        sut.PressMouseButtonCommand.Execute("left");

        Assert.Single(ws.SentMessages);
        Assert.Contains("\"side\":\"left\"", ws.SentMessages[0]);
        Assert.Contains("\"pressed\":true", ws.SentMessages[0]);
    }

    [Fact]
    public void PressMouseButtonCommand_WhenNotConnected_DoesNotSend()
    {
        FakeInputWebSocketService ws = new();
        using VirtualTouchPadViewModel sut = new(null, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        sut.PressMouseButtonCommand.Execute("left");

        Assert.Empty(ws.SentMessages);
    }

    [Fact]
    public async Task OnTouchPadDelta_WhenRapidBurst_CoalescesMessagesAndPreservesTotalMovement()
    {
        FakeInputWebSocketService ws = new() { SendDelay = TimeSpan.FromMilliseconds(15) };
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token", mouseSpeedMultiplier: 1.0);
        using VirtualTouchPadViewModel sut = new(settings, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        sut.Activate();
        bool connected = SpinWait.SpinUntil(() => sut.IsConnected, TimeSpan.FromSeconds(5));
        Assert.True(connected);

        ws.SentMessages.Clear();
        for (int i = 0; i < 40; i++)
        {
            sut.OnTouchPadDelta(1, -1);
        }

        bool received = SpinWait.SpinUntil(() => ws.SentMessages.Count > 0, TimeSpan.FromSeconds(5));
        Assert.True(received);

        // Allow any in-flight coalesced send to complete so totals are stable.
        await Task.Delay(120);

        int totalDx = 0;
        int totalDy = 0;
        foreach (string payload in ws.SentMessages)
        {
            var node = JsonNode.Parse(payload);
            totalDx += node?["dx"]?.GetValue<int>() ?? 0;
            totalDy += node?["dy"]?.GetValue<int>() ?? 0;
        }

        Assert.Equal(40, totalDx);
        Assert.Equal(-40, totalDy);
        Assert.True(ws.SentMessages.Count < 40);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static AppGlobalSettings CreateSettings(string baseUrl, string token, double mouseSpeedMultiplier = 4.0)
    {
        AppConfigRoot config = new()
        {
            Runtime = new RuntimeAppConfig
            {
                ServerApiBaseUrl = baseUrl,
                ServerApiAuthToken = token,
                MouseSpeedMultiplier = mouseSpeedMultiplier,
            },
        };

        return new AppGlobalSettings(new FakeSettingsOrchestrator(config));
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeOrientationService : IOrientationService
    {
        public int RequestLandscapeCallCount { get; private set; }
        public int RestoreDefaultCallCount { get; private set; }

        public void RequestLandscape() => RequestLandscapeCallCount++;

        public void RestoreDefault() => RestoreDefaultCallCount++;
    }

    private sealed class FakeInputWebSocketService : IInputWebSocketService
    {
        public bool IsConnected { get; private set; }
        public string? LastDeviceName { get; private set; }
        public List<string> SentMessages { get; } = [];
        public TimeSpan SendDelay { get; init; }

        public Task ConnectAsync(string baseUrl, string bearerToken, string path, string deviceName, CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            LastDeviceName = deviceName;
            return Task.CompletedTask;
        }

        public async Task SendAsync<T>(T message, CancellationToken cancellationToken = default)
        {
            if (SendDelay > TimeSpan.Zero)
            {
                await Task.Delay(SendDelay, cancellationToken);
            }

            SentMessages.Add(JsonSerializer.Serialize(message));
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    private sealed class FakeDeviceDisplayNameProvider(string deviceName = "Test Android") : IDeviceDisplayNameProvider
    {
        public string GetDisplayName() => deviceName;
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
}
