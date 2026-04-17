using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using ChartHub.Server.Contracts;
using ChartHub.Server.Endpoints;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Tests;

public sealed class InputEndpointsIntegrationTests
{
    // ── Auth guard ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ControllerWebSocketRequiresAuth()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(authenticatedClient: false);

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/input/controller/ws");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TouchpadWebSocketRequiresAuth()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(authenticatedClient: false);

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/input/touchpad/ws");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task KeyboardWebSocketRequiresAuth()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(authenticatedClient: false);

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/input/keyboard/ws");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Non-WebSocket upgrade returns 400 ─────────────────────────────────────

    [Fact]
    public async Task ControllerEndpointReturnsBadRequestForPlainHttp()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/input/controller/ws");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TouchpadEndpointReturnsBadRequestForPlainHttp()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/input/touchpad/ws");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task KeyboardEndpointReturnsBadRequestForPlainHttp()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();

        HttpResponseMessage response = await fixture.Client.GetAsync("/api/v1/input/keyboard/ws");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── WebSocket connects and dispatches messages ────────────────────────────

    [Fact]
    public async Task ControllerWebSocketDispatchesButtonMessage()
    {
        FakeGamepadService fakeGamepad = new();
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(gamepad: fakeGamepad);
        WebSocketClient wsClient = fixture.CreateWebSocketClient();

        using WebSocket ws = await wsClient.ConnectAsync(
            new Uri("ws://localhost/api/v1/input/controller/ws"), CancellationToken.None);

        await SendJsonAsync(ws, new ControllerButtonMessage { ButtonId = "a", Pressed = true });
        await Task.Delay(50); // allow server handler to process

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);

        Assert.Single(fakeGamepad.ButtonPresses);
        Assert.Equal("a", fakeGamepad.ButtonPresses[0].ButtonId);
        Assert.True(fakeGamepad.ButtonPresses[0].Pressed);
    }

    [Fact]
    public async Task ControllerWebSocketDispatchesDPadMessage()
    {
        FakeGamepadService fakeGamepad = new();
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(gamepad: fakeGamepad);
        WebSocketClient wsClient = fixture.CreateWebSocketClient();

        using WebSocket ws = await wsClient.ConnectAsync(
            new Uri("ws://localhost/api/v1/input/controller/ws"), CancellationToken.None);

        await SendJsonAsync(ws, new ControllerDPadMessage { X = 1, Y = 0 });
        await Task.Delay(50);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);

        Assert.Single(fakeGamepad.DPadInputs);
        Assert.Equal(1, fakeGamepad.DPadInputs[0].X);
        Assert.Equal(0, fakeGamepad.DPadInputs[0].Y);
    }

    [Fact]
    public async Task TouchpadWebSocketDispatchesMoveMessage()
    {
        FakeMouseService fakeMouse = new();
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(mouse: fakeMouse);
        WebSocketClient wsClient = fixture.CreateWebSocketClient();

        using WebSocket ws = await wsClient.ConnectAsync(
            new Uri("ws://localhost/api/v1/input/touchpad/ws"), CancellationToken.None);

        await SendJsonAsync(ws, new TouchpadMoveMessage { Dx = 5, Dy = -3 });
        await Task.Delay(50);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);

        Assert.Single(fakeMouse.Moves);
        Assert.Equal(5, fakeMouse.Moves[0].Dx);
        Assert.Equal(-3, fakeMouse.Moves[0].Dy);
    }

    [Fact]
    public async Task KeyboardWebSocketDispatchesCharMessage()
    {
        FakeKeyboardService fakeKeyboard = new();
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(keyboard: fakeKeyboard);
        WebSocketClient wsClient = fixture.CreateWebSocketClient();

        using WebSocket ws = await wsClient.ConnectAsync(
            new Uri("ws://localhost/api/v1/input/keyboard/ws"), CancellationToken.None);

        await SendJsonAsync(ws, new KeyboardCharMessage { Char = "h" });
        await Task.Delay(50);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);

        Assert.Single(fakeKeyboard.TypedChars);
        Assert.Equal('h', fakeKeyboard.TypedChars[0]);
    }

    [Fact]
    public async Task KeyboardWebSocketDispatchesKeyMessage()
    {
        FakeKeyboardService fakeKeyboard = new();
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync(keyboard: fakeKeyboard);
        WebSocketClient wsClient = fixture.CreateWebSocketClient();

        using WebSocket ws = await wsClient.ConnectAsync(
            new Uri("ws://localhost/api/v1/input/keyboard/ws"), CancellationToken.None);

        await SendJsonAsync(ws, new KeyboardKeyMessage { LinuxKeyCode = 28, Pressed = true });
        await Task.Delay(50);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);

        Assert.Single(fakeKeyboard.KeyPresses);
        Assert.Equal(28, fakeKeyboard.KeyPresses[0].LinuxKeyCode);
        Assert.True(fakeKeyboard.KeyPresses[0].Pressed);
    }

    // ── WebSocket close is handled gracefully ─────────────────────────────────

    [Fact]
    public async Task ControllerWebSocketHandlesClientClose()
    {
        await using TestAppFixture fixture = await TestAppFixture.CreateAsync();
        WebSocketClient wsClient = fixture.CreateWebSocketClient();

        using WebSocket ws = await wsClient.ConnectAsync(
            new Uri("ws://localhost/api/v1/input/controller/ws"), CancellationToken.None);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        Assert.Equal(WebSocketState.Closed, ws.State);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task SendJsonAsync<T>(WebSocket ws, T message)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    // ── TestAppFixture ────────────────────────────────────────────────────────

    private sealed class TestAppFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TestAppFixture(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public WebSocketClient CreateWebSocketClient()
        {
            WebSocketClient wsClient = _app.GetTestServer().CreateWebSocketClient();
            wsClient.ConfigureRequest = req =>
            {
                req.Headers["Authorization"] = "Test";
            };
            return wsClient;
        }

        public static async Task<TestAppFixture> CreateAsync(
            FakeGamepadService? gamepad = null,
            FakeMouseService? mouse = null,
            FakeKeyboardService? keyboard = null,
            bool authenticatedClient = true)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();

            builder.Services
                .AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            builder.Services.AddAuthorization();

            builder.Services.AddSingleton<IUinputGamepadService>(gamepad ?? new FakeGamepadService());
            builder.Services.AddSingleton<IUinputMouseService>(mouse ?? new FakeMouseService());
            builder.Services.AddSingleton<IUinputKeyboardService>(keyboard ?? new FakeKeyboardService());
            builder.Services.AddSingleton<IInputConnectionTracker, InputConnectionTracker>();

            WebApplication app = builder.Build();
            app.UseWebSockets();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapInputEndpoints();

            await app.StartAsync();

            HttpClient client = app.GetTestClient();
            if (authenticatedClient)
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
            }

            return new TestAppFixture(app, client);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    // ── Fake services ─────────────────────────────────────────────────────────

    private sealed class FakeGamepadService : IUinputGamepadService
    {
        public List<(string ButtonId, bool Pressed)> ButtonPresses { get; } = [];
        public List<(int X, int Y)> DPadInputs { get; } = [];

        public bool IsSupported => true;

        public void PressButton(string buttonId, bool pressed)
            => ButtonPresses.Add((buttonId, pressed));

        public void SetDPad(int x, int y)
            => DPadInputs.Add((x, y));
    }

    private sealed class FakeMouseService : IUinputMouseService
    {
        public List<(int Dx, int Dy)> Moves { get; } = [];
        public List<(string Side, bool Pressed)> ButtonPresses { get; } = [];

        public bool IsSupported => true;

        public void MoveDelta(int dx, int dy) => Moves.Add((dx, dy));

        public void PressButton(string side, bool pressed) => ButtonPresses.Add((side, pressed));
    }

    private sealed class FakeKeyboardService : IUinputKeyboardService
    {
        public List<char> TypedChars { get; } = [];
        public List<(int LinuxKeyCode, bool Pressed)> KeyPresses { get; } = [];

        public bool IsSupported => true;

        public void TypeChar(char c) => TypedChars.Add(c);

        public void PressKey(int linuxKeyCode, bool pressed) => KeyPresses.Add((linuxKeyCode, pressed));
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out Microsoft.Extensions.Primitives.StringValues value)
                || value.Count == 0)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            ClaimsIdentity identity = new(
            [
                new Claim(ClaimTypes.NameIdentifier, "test-user"),
                new Claim(ClaimTypes.Name, "test-user"),
            ],
            "Test");

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
