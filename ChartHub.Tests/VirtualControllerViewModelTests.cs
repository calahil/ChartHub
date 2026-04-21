using System.Text.Json;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class VirtualControllerViewModelTests
{
    [Fact]
    public void Constructor_InitialisesDisconnectedStatusAndCommands()
    {
        using VirtualControllerViewModel sut = new(null, new FakeInputWebSocketService(), new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        Assert.False(sut.IsConnected);
        Assert.NotEmpty(sut.StatusMessage);
        Assert.NotNull(sut.PressButtonCommand);
        Assert.NotNull(sut.ReleaseButtonCommand);
        Assert.NotNull(sut.SetDPadCommand);
        Assert.NotNull(sut.ActivateCommand);
        Assert.NotNull(sut.DeactivateCommand);
    }

    [Fact]
    public void Activate_RequestsLandscapeOrientation()
    {
        FakeOrientationService orientation = new();
        using VirtualControllerViewModel sut = new(null, new FakeInputWebSocketService(), orientation, new FakeDeviceDisplayNameProvider());

        sut.Activate();

        Assert.Equal(1, orientation.RequestLandscapeCallCount);
    }

    [Fact]
    public void Deactivate_SetsIsConnectedFalseAndRestoresOrientation()
    {
        FakeOrientationService orientation = new();
        using VirtualControllerViewModel sut = new(null, new FakeInputWebSocketService(), orientation, new FakeDeviceDisplayNameProvider());

        sut.Deactivate();

        Assert.False(sut.IsConnected);
        Assert.Equal(1, orientation.RestoreDefaultCallCount);
    }

    [Fact]
    public void PressButtonCommand_WhenNotConnected_DoesNotSendToWebSocket()
    {
        FakeInputWebSocketService ws = new();
        using VirtualControllerViewModel sut = new(null, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        sut.PressButtonCommand.Execute("a");

        Assert.Empty(ws.SentMessages);
    }

    [Fact]
    public void ReleaseButtonCommand_WhenNotConnected_DoesNotSendToWebSocket()
    {
        FakeInputWebSocketService ws = new();
        using VirtualControllerViewModel sut = new(null, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        sut.ReleaseButtonCommand.Execute("b");

        Assert.Empty(ws.SentMessages);
    }

    [Fact]
    public void SetDPadCommand_WhenNotConnected_DoesNotSendToWebSocket()
    {
        FakeInputWebSocketService ws = new();
        using VirtualControllerViewModel sut = new(null, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        sut.SetDPadCommand.Execute("0,-1");

        Assert.Empty(ws.SentMessages);
    }

    [Fact]
    public void Activate_WithValidBaseUrl_ConnectsAndSetsIsConnected()
    {
        FakeInputWebSocketService ws = new();
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token");
        using VirtualControllerViewModel sut = new(settings, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider("Pixel 8"));

        sut.Activate();
        bool connected = SpinWait.SpinUntil(() => sut.IsConnected, TimeSpan.FromSeconds(5));

        Assert.True(connected);
        Assert.True(ws.ConnectCallCount >= 1);
        Assert.Equal("Pixel 8", ws.LastDeviceName);
    }

    [Fact]
    public void Activate_WithEmptyBaseUrl_DoesNotConnect()
    {
        FakeInputWebSocketService ws = new();
        using VirtualControllerViewModel sut = new(null, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        sut.Activate();

        Assert.Equal(0, ws.ConnectCallCount);
        Assert.False(sut.IsConnected);
    }

    [Fact]
    public void PressButtonCommand_WhenConnected_SendsButtonPressMessage()
    {
        FakeInputWebSocketService ws = new();
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token");
        using VirtualControllerViewModel sut = new(settings, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        sut.Activate();
        bool connected = SpinWait.SpinUntil(() => sut.IsConnected, TimeSpan.FromSeconds(5));
        Assert.True(connected);

        ws.SentMessages.Clear();
        sut.PressButtonCommand.Execute("a");

        Assert.Single(ws.SentMessages);
        Assert.Contains("\"buttonId\":\"a\"", ws.SentMessages[0]);
        Assert.Contains("\"pressed\":true", ws.SentMessages[0]);
    }

    [Fact]
    public void SetDPadCommand_WhenConnected_SendsDPadMessage()
    {
        FakeInputWebSocketService ws = new();
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token");
        using VirtualControllerViewModel sut = new(settings, ws, new FakeOrientationService(), new FakeDeviceDisplayNameProvider());

        sut.Activate();
        bool connected = SpinWait.SpinUntil(() => sut.IsConnected, TimeSpan.FromSeconds(5));
        Assert.True(connected);

        ws.SentMessages.Clear();
        sut.SetDPadCommand.Execute("0,-1");

        Assert.Single(ws.SentMessages);
        Assert.Contains("\"x\":0", ws.SentMessages[0]);
        Assert.Contains("\"y\":-1", ws.SentMessages[0]);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static AppGlobalSettings CreateSettings(string baseUrl, string token)
    {
        AppConfigRoot config = new()
        {
            Runtime = new RuntimeAppConfig
            {
                ServerApiBaseUrl = baseUrl,
                ServerApiAuthToken = token,
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
        public int ConnectCallCount { get; private set; }
        public string? LastDeviceName { get; private set; }
        public List<string> SentMessages { get; } = [];

        public Task ConnectAsync(string baseUrl, string bearerToken, string path, string deviceName, CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            ConnectCallCount++;
            LastDeviceName = deviceName;
            return Task.CompletedTask;
        }

        public Task SendAsync<T>(T message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(JsonSerializer.Serialize(message));
            return Task.CompletedTask;
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
