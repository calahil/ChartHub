using System.Text.Json;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class VirtualKeyboardViewModelTests
{
    [Fact]
    public void Constructor_InitialisesDisconnectedStatusAndCommands()
    {
        using VirtualKeyboardViewModel sut = new(null, new FakeInputWebSocketService(), new FakeDeviceDisplayNameProvider());

        Assert.False(sut.IsConnected);
        Assert.NotEmpty(sut.StatusMessage);
        Assert.Equal(string.Empty, sut.InputBuffer);
        Assert.NotNull(sut.SendEnterCommand);
        Assert.NotNull(sut.SendBackspaceCommand);
        Assert.NotNull(sut.SendEscapeCommand);
        Assert.NotNull(sut.SendTabCommand);
    }

    [Fact]
    public void Deactivate_SetsIsConnectedFalseAndUpdatesStatus()
    {
        FakeInputWebSocketService ws = new();
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token");
        using VirtualKeyboardViewModel sut = new(settings, ws, new FakeDeviceDisplayNameProvider("Pixel 8"));

        sut.Activate();
        bool connected = SpinWait.SpinUntil(() => sut.IsConnected, TimeSpan.FromSeconds(5));
        Assert.True(connected);
        Assert.Equal("Pixel 8", ws.LastDeviceName);

        sut.Deactivate();

        Assert.False(sut.IsConnected);
        Assert.NotEmpty(sut.StatusMessage);
    }

    [Fact]
    public void InputBuffer_WhenNotConnected_DoesNotSendCharsToWebSocket()
    {
        FakeInputWebSocketService ws = new();
        using VirtualKeyboardViewModel sut = new(null, ws, new FakeDeviceDisplayNameProvider());

        sut.InputBuffer = "hello";

        Assert.Empty(ws.SentMessages);
    }

    [Fact]
    public void InputBuffer_WhenConnected_SendsNewCharsToWebSocket()
    {
        FakeInputWebSocketService ws = new();
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token");
        using VirtualKeyboardViewModel sut = new(settings, ws, new FakeDeviceDisplayNameProvider());

        sut.Activate();
        bool connected = SpinWait.SpinUntil(() => sut.IsConnected, TimeSpan.FromSeconds(5));
        Assert.True(connected);

        ws.SentMessages.Clear();
        sut.InputBuffer = "ab";

        // One SendAsync call per character appended.
        Assert.Equal(2, ws.SentMessages.Count);
        Assert.Contains("\"char\":\"a\"", ws.SentMessages[0]);
        Assert.Contains("\"type\":\"char\"", ws.SentMessages[0]);
        Assert.Contains("\"char\":\"b\"", ws.SentMessages[1]);
    }

    [Fact]
    public void InputBuffer_WhenConnected_DeletionDoesNotSendChars()
    {
        FakeInputWebSocketService ws = new();
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token");
        using VirtualKeyboardViewModel sut = new(settings, ws, new FakeDeviceDisplayNameProvider());

        sut.Activate();
        bool connected = SpinWait.SpinUntil(() => sut.IsConnected, TimeSpan.FromSeconds(5));
        Assert.True(connected);

        // Append to establish buffer, then clear tracking.
        sut.InputBuffer = "hello";
        ws.SentMessages.Clear();

        // Delete one character (shorter string).
        sut.InputBuffer = "hell";

        Assert.Empty(ws.SentMessages);
    }

    [Fact]
    public void SendEnterCommand_WhenNotConnected_DoesNotSendToWebSocket()
    {
        FakeInputWebSocketService ws = new();
        using VirtualKeyboardViewModel sut = new(null, ws, new FakeDeviceDisplayNameProvider());

        sut.SendEnterCommand.Execute(null);

        Assert.Empty(ws.SentMessages);
    }

    [Fact]
    public void SendEnterCommand_WhenConnected_SendsPressAndReleasePair()
    {
        FakeInputWebSocketService ws = new();
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token");
        using VirtualKeyboardViewModel sut = new(settings, ws, new FakeDeviceDisplayNameProvider());

        sut.Activate();
        bool connected = SpinWait.SpinUntil(() => sut.IsConnected, TimeSpan.FromSeconds(5));
        Assert.True(connected);

        ws.SentMessages.Clear();
        sut.SendEnterCommand.Execute(null);

        // Press + release = 2 messages
        Assert.Equal(2, ws.SentMessages.Count);
        Assert.Contains("\"pressed\":true", ws.SentMessages[0]);
        Assert.Contains("\"pressed\":false", ws.SentMessages[1]);
    }

    [Fact]
    public void Activate_WithEmptyBaseUrl_DoesNotConnect()
    {
        FakeInputWebSocketService ws = new();
        using VirtualKeyboardViewModel sut = new(null, ws, new FakeDeviceDisplayNameProvider());

        sut.Activate();

        Assert.Equal(0, ws.ConnectCallCount);
        Assert.False(sut.IsConnected);
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
