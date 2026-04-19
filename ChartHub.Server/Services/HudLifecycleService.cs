using System.Diagnostics;

using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed partial class HudLifecycleService(
    IOptions<HudOptions> options,
    ILogger<HudLifecycleService> logger) : IHudLifecycleService, IHostedService, IDisposable
{
    private readonly HudOptions _options = options.Value;
    private readonly ILogger<HudLifecycleService> _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _hudProcess;
    private bool _suspended;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            LogHudDisabled(_logger);
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SpawnHud();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            KillHudProcess();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SuspendAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _suspended = true;
            KillHudProcess();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_suspended)
            {
                return;
            }

            _suspended = false;
            SpawnHud();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
        _hudProcess?.Dispose();
    }

    // Must be called with _gate held.
    private void SpawnHud()
    {
        if (IsHudRunning())
        {
            return;
        }

        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _options.ExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            },
            EnableRaisingEvents = true,
        };

        // Forward X display variables explicitly so the Hud can open a window
        // even if the environment is altered between server startup and Hud spawn.
        string? display = Environment.GetEnvironmentVariable("DISPLAY");
        string? xauthority = Environment.GetEnvironmentVariable("XAUTHORITY");
        if (display is not null)
        {
            process.StartInfo.Environment["DISPLAY"] = display;
        }

        if (xauthority is not null)
        {
            process.StartInfo.Environment["XAUTHORITY"] = xauthority;
        }

        process.StartInfo.ArgumentList.Add("--server-port");
        process.StartInfo.ArgumentList.Add(_options.ServerPort.ToString());

        process.Exited += OnHudExited;

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                LogHudStartFailed(_logger, _options.ExecutablePath);
                return;
            }

            _hudProcess = process;
            LogHudStarted(_logger, _options.ExecutablePath, process.Id);
        }
        catch (Exception ex)
        {
            process.Dispose();
            LogHudStartException(_logger, _options.ExecutablePath, ex);
        }
    }

    // Must be called with _gate held.
    private void KillHudProcess()
    {
        if (_hudProcess is null)
        {
            return;
        }

        try
        {
            if (!_hudProcess.HasExited)
            {
                _hudProcess.Kill(entireProcessTree: false);
                LogHudKilled(_logger, _hudProcess.Id);
            }
        }
        catch (Exception ex)
        {
            LogHudKillException(_logger, ex);
        }
        finally
        {
            _hudProcess.Dispose();
            _hudProcess = null;
        }
    }

    private bool IsHudRunning()
    {
        if (_hudProcess is null)
        {
            return false;
        }

        try
        {
            return !_hudProcess.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private void OnHudExited(object? sender, EventArgs e)
    {
        LogHudExited(_logger);

        // Clean up without re-spawning — re-spawn is driven by ResumeAsync after game exits.
        _hudProcess?.Dispose();
        _hudProcess = null;
    }

    [LoggerMessage(EventId = 9001, Level = LogLevel.Information, Message = "HudLifecycleService: HUD disabled (Hud:ExecutablePath not configured).")]
    private static partial void LogHudDisabled(ILogger logger);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Information, Message = "HudLifecycleService: HUD process started. Path={Path} PID={Pid}")]
    private static partial void LogHudStarted(ILogger logger, string path, int pid);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Warning, Message = "HudLifecycleService: Process.Start() returned false for Path={Path}.")]
    private static partial void LogHudStartFailed(ILogger logger, string path);

    [LoggerMessage(EventId = 9004, Level = LogLevel.Error, Message = "HudLifecycleService: Exception starting HUD at Path={Path}.")]
    private static partial void LogHudStartException(ILogger logger, string path, Exception ex);

    [LoggerMessage(EventId = 9005, Level = LogLevel.Information, Message = "HudLifecycleService: HUD process killed (PID={Pid}).")]
    private static partial void LogHudKilled(ILogger logger, int pid);

    [LoggerMessage(EventId = 9006, Level = LogLevel.Warning, Message = "HudLifecycleService: Exception while killing HUD process.")]
    private static partial void LogHudKillException(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 9007, Level = LogLevel.Information, Message = "HudLifecycleService: HUD process exited.")]
    private static partial void LogHudExited(ILogger logger);
}
