using ChartHub.Server.Contracts;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChartHub.Server.Tests;

public sealed class VolumeServiceTests
{
    [Fact]
    public async Task GetStateAsyncReturnsMasterAndSessionState()
    {
        FakeProcessRunner runner = new();
        runner.AddResult("pactl", ["info"], new VolumeService.ProcessExecutionResult(0, "Server String: /tmp/pulse\n", string.Empty));
        runner.AddResult("pactl", ["get-sink-volume", "@DEFAULT_SINK@"], new VolumeService.ProcessExecutionResult(0, "Volume: front-left: 44040 / 67% / -10.62 dB,   front-right: 44040 / 67% / -10.62 dB\n", string.Empty));
        runner.AddResult("pactl", ["get-sink-mute", "@DEFAULT_SINK@"], new VolumeService.ProcessExecutionResult(0, "Mute: no\n", string.Empty));
        runner.AddResult("pactl", ["list", "sink-inputs"], new VolumeService.ProcessExecutionResult(0, "Sink Input #42\n    Volume: front-left: 32768 / 50% / -18.06 dB,   front-right: 32768 / 50% / -18.06 dB\n    Mute: no\n    Property List:\n        application.name = \"RetroArch\"\n        media.name = \"Game Audio\"\n        application.process.id = \"1234\"\n", string.Empty));

        VolumeService sut = new(NullLogger<VolumeService>.Instance, runner);

        VolumeStateResponse state = await sut.GetStateAsync(CancellationToken.None);

        Assert.Equal(67, state.Master.ValuePercent);
        Assert.False(state.Master.IsMuted);
        Assert.True(state.SupportsPerApplicationSessions);
        VolumeSessionResponse session = Assert.Single(state.Sessions);
        Assert.Equal("42", session.SessionId);
        Assert.Equal("Game Audio", session.Name);
        Assert.Equal(1234, session.ProcessId);
        Assert.Equal(50, session.ValuePercent);
    }

    [Fact]
    public async Task GetStateAsyncWithoutPactlFallsBackToAmixer()
    {
        FakeProcessRunner runner = new();
        runner.AddResult("pactl", ["get-sink-volume", "@DEFAULT_SINK@"], new VolumeService.ProcessExecutionResult(127, string.Empty, "pactl not found"));
        runner.AddResult("amixer", ["get", "Master"], new VolumeService.ProcessExecutionResult(0, "Mono: Playback 43691 [67%] [-21.00dB] [on]\n", string.Empty));
        runner.AddResult("pactl", ["list", "sink-inputs"], new VolumeService.ProcessExecutionResult(127, string.Empty, "pactl not found"));

        VolumeService sut = new(NullLogger<VolumeService>.Instance, runner);

        VolumeStateResponse state = await sut.GetStateAsync(CancellationToken.None);

        Assert.Equal(67, state.Master.ValuePercent);
        Assert.False(state.SupportsPerApplicationSessions);
        Assert.Empty(state.Sessions);
    }

    [Fact]
    public async Task SetMasterVolumeAsyncViaPactlUpdatesVolume()
    {
        FakeProcessRunner runner = new();
        runner.AddResult("pactl", ["info"], new VolumeService.ProcessExecutionResult(0, "Server String: /tmp/pulse\n", string.Empty));
        runner.AddResult("pactl", ["set-sink-volume", "@DEFAULT_SINK@", "50%"], new VolumeService.ProcessExecutionResult(0, string.Empty, string.Empty));
        runner.AddResult("pactl", ["get-sink-volume", "@DEFAULT_SINK@"], new VolumeService.ProcessExecutionResult(0, "Volume: front-left: 32768 / 50% / -18.06 dB,   front-right: 32768 / 50% / -18.06 dB\n", string.Empty));
        runner.AddResult("pactl", ["get-sink-mute", "@DEFAULT_SINK@"], new VolumeService.ProcessExecutionResult(0, "Mute: no\n", string.Empty));

        VolumeService sut = new(NullLogger<VolumeService>.Instance, runner);

        VolumeActionResponse response = await sut.SetMasterVolumeAsync(50, CancellationToken.None);

        Assert.Equal(50, response.ValuePercent);
        Assert.False(response.IsMuted);
        Assert.Equal("master", response.TargetKind);
    }

    [Fact]
    public async Task SetSessionVolumeAsyncWithoutPactlThrowsUnsupported()
    {
        FakeProcessRunner runner = new();
        runner.AddResult("pactl", ["info"], new VolumeService.ProcessExecutionResult(127, string.Empty, "pactl not found"));

        VolumeService sut = new(NullLogger<VolumeService>.Instance, runner);

        VolumeServiceException ex = await Assert.ThrowsAsync<VolumeServiceException>(() =>
            sut.SetSessionVolumeAsync("42", 50, CancellationToken.None));

        Assert.Equal(StatusCodes.Status501NotImplemented, ex.StatusCode);
        Assert.Equal("session_volume_unsupported", ex.ErrorCode);
    }

    [Fact]
    public async Task SetMasterVolumeAsyncWhenAmixerUnavailableThrowsNotImplemented()
    {
        FakeProcessRunner runner = new();
        runner.AddResult("pactl", ["set-sink-volume", "@DEFAULT_SINK@", "50%"], new VolumeService.ProcessExecutionResult(127, string.Empty, "pactl not found"));
        runner.AddResult("amixer", ["set", "Master", "50%"], new VolumeService.ProcessExecutionResult(1, string.Empty, "Unable to find simple control 'Master'"));

        VolumeService sut = new(NullLogger<VolumeService>.Instance, runner);

        VolumeServiceException ex = await Assert.ThrowsAsync<VolumeServiceException>(() =>
            sut.SetMasterVolumeAsync(50, CancellationToken.None));

        Assert.Equal(StatusCodes.Status501NotImplemented, ex.StatusCode);
        Assert.Equal("master_volume_unavailable", ex.ErrorCode);
    }

    [Fact]
    public async Task GetStateAsyncWhenMasterAndSessionsUnavailableReturnsDefaultState()
    {
        FakeProcessRunner runner = new();
        runner.AddResult("pactl", ["get-sink-volume", "@DEFAULT_SINK@"], new VolumeService.ProcessExecutionResult(127, string.Empty, "Connection failure"));
        runner.AddResult("amixer", ["get", "Master"], new VolumeService.ProcessExecutionResult(1, string.Empty, "Unable to find simple control 'Master'"));
        runner.AddResult("pactl", ["list", "sink-inputs"], new VolumeService.ProcessExecutionResult(127, string.Empty, "Connection failure"));

        VolumeService sut = new(NullLogger<VolumeService>.Instance, runner);

        VolumeStateResponse state = await sut.GetStateAsync(CancellationToken.None);

        Assert.Equal(0, state.Master.ValuePercent);
        Assert.False(state.SupportsPerApplicationSessions);
        Assert.Empty(state.Sessions);
        Assert.NotNull(state.SessionSupportMessage);
    }

    private sealed class FakeProcessRunner : VolumeService.IVolumeProcessRunner
    {
        private readonly Dictionary<string, Queue<VolumeService.ProcessExecutionResult>> _results = new(StringComparer.Ordinal);

        public void AddResult(string fileName, IReadOnlyList<string> arguments, VolumeService.ProcessExecutionResult result)
        {
            string key = BuildKey(fileName, arguments);
            if (!_results.TryGetValue(key, out Queue<VolumeService.ProcessExecutionResult>? queue))
            {
                queue = new Queue<VolumeService.ProcessExecutionResult>();
                _results[key] = queue;
            }

            queue.Enqueue(result);
        }

        public Task<VolumeService.ProcessExecutionResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            string key = BuildKey(fileName, arguments);
            if (!_results.TryGetValue(key, out Queue<VolumeService.ProcessExecutionResult>? queue)
                || queue.Count == 0)
            {
                throw new InvalidOperationException($"No fake process result configured for {key}.");
            }

            return Task.FromResult(queue.Dequeue());
        }

        private static string BuildKey(string fileName, IReadOnlyList<string> arguments)
        {
            return string.Join('\u001F', new[] { fileName }.Concat(arguments));
        }
    }
}