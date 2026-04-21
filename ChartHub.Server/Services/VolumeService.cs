using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

using ChartHub.Server.Contracts;

namespace ChartHub.Server.Services;

public sealed partial class VolumeService(ILogger<VolumeService> logger) : IVolumeService
{
    private static readonly Regex PercentageRegex = new(@"(\d{1,3})%", RegexOptions.Compiled);
    private readonly IVolumeProcessRunner _processRunner = new DefaultVolumeProcessRunner();
    private readonly ILogger<VolumeService> _logger = logger;
    private readonly object _changeSync = new();
    private TaskCompletionSource<long> _changeSignal = CreateChangeSignal();
    private long _changeStamp;

    internal VolumeService(ILogger<VolumeService> logger, IVolumeProcessRunner processRunner)
        : this(logger)
    {
        _processRunner = processRunner;
    }

    public bool IsSupportedPlatform => OperatingSystem.IsLinux();

    public int SseHeartbeatSeconds => 2;

    public long CurrentChangeStamp => Interlocked.Read(ref _changeStamp);

    public async Task<VolumeStateResponse> GetStateAsync(CancellationToken cancellationToken)
    {
        EnsureLinux();

        (VolumeMasterStateResponse master, string? masterSupportMessage) = await GetMasterVolumeBestEffortAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<VolumeSessionResponse> sessions = [];
        bool supportsPerApplicationSessions = false;
        string? sessionSupportMessage = masterSupportMessage;

        try
        {
            sessions = await ListSessionsAsync(cancellationToken).ConfigureAwait(false);
            supportsPerApplicationSessions = true;
        }
        catch (VolumeServiceException)
        {
            sessionSupportMessage = CombineSupportMessages(
                masterSupportMessage,
                "Per-application volume requires pactl on the server host.");
        }

        return new VolumeStateResponse
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Master = master,
            SupportsMasterVolume = string.IsNullOrWhiteSpace(masterSupportMessage),
            Sessions = sessions,
            SupportsPerApplicationSessions = supportsPerApplicationSessions,
            SessionSupportMessage = sessionSupportMessage,
        };
    }

    public async Task<VolumeActionResponse> SetMasterVolumeAsync(int valuePercent, CancellationToken cancellationToken)
    {
        EnsureLinux();
        EnsureValidVolume(valuePercent);

        VolumeMasterStateResponse master;

        ProcessExecutionResult pactlResult = await _processRunner
            .RunAsync("pactl", ["set-sink-volume", "@DEFAULT_SINK@", $"{valuePercent}%"], cancellationToken)
            .ConfigureAwait(false);

        if (pactlResult.ExitCode == 0)
        {
            master = await GetMasterVolumeViaPactlAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            ProcessExecutionResult result = await _processRunner
                .RunAsync("amixer", ["set", "Master", $"{valuePercent}%"], cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                LogMasterVolumeUpdateFailed(_logger, result.StandardError);
                throw new VolumeServiceException(
                    StatusCodes.Status501NotImplemented,
                    "master_volume_unavailable",
                    "Master volume control is unavailable on this server host.");
            }

            master = await GetMasterVolumeViaAmixerAsync(cancellationToken).ConfigureAwait(false);
        }

        NotifyChanged();

        return new VolumeActionResponse
        {
            TargetId = "master",
            TargetKind = "master",
            Name = "Master Volume",
            ValuePercent = master.ValuePercent,
            IsMuted = master.IsMuted,
            Message = "Master volume updated.",
        };
    }

    public async Task<VolumeActionResponse> SetSessionVolumeAsync(string sessionId, int valuePercent, CancellationToken cancellationToken)
    {
        EnsureLinux();
        EnsureValidVolume(valuePercent);

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new VolumeServiceException(StatusCodes.Status400BadRequest, "validation_error", "sessionId is required.");
        }

        if (!await IsPactlAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new VolumeServiceException(
                StatusCodes.Status501NotImplemented,
                "session_volume_unsupported",
                "Per-application volume control requires pactl on the server host.");
        }

        ProcessExecutionResult result = await _processRunner
            .RunAsync("pactl", ["set-sink-input-volume", sessionId, $"{valuePercent}%"], cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            if (ContainsNoSuchEntity(result))
            {
                throw new VolumeServiceException(StatusCodes.Status404NotFound, "session_not_found", $"Volume session '{sessionId}' was not found.");
            }

            LogSessionVolumeUpdateFailed(_logger, sessionId, result.StandardError);
            throw new VolumeServiceException(
                StatusCodes.Status500InternalServerError,
                "session_volume_update_failed",
                "Failed to update per-application volume.");
        }

        VolumeSessionResponse session = await GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        NotifyChanged();

        return new VolumeActionResponse
        {
            TargetId = session.SessionId,
            TargetKind = "session",
            Name = session.Name,
            ValuePercent = session.ValuePercent,
            IsMuted = session.IsMuted,
            Message = "Per-application volume updated.",
        };
    }

    public async Task<bool> WaitForChangeAsync(long observedChangeStamp, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (CurrentChangeStamp != observedChangeStamp)
        {
            return true;
        }

        TaskCompletionSource<long> signal;
        lock (_changeSync)
        {
            signal = _changeSignal;
        }

        Task completed = await Task.WhenAny(signal.Task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
        return completed == signal.Task;
    }

    private async Task<VolumeMasterStateResponse> GetMasterVolumeViaAmixerAsync(CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _processRunner
            .RunAsync("amixer", ["get", "Master"], cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            LogMasterVolumeLookupFailed(_logger, result.StandardError);
            throw new VolumeServiceException(
                StatusCodes.Status501NotImplemented,
                "master_volume_unavailable",
                "Master volume control is unavailable on this server host.");
        }

        MatchCollection matches = PercentageRegex.Matches(result.StandardOutput);
        if (matches.Count == 0 || !int.TryParse(matches[^1].Groups[1].Value, out int valuePercent))
        {
            throw new VolumeServiceException(
                StatusCodes.Status500InternalServerError,
                "master_volume_parse_failed",
                "Failed to parse master volume from amixer output.");
        }

        bool isMuted = result.StandardOutput.Contains("[off]", StringComparison.OrdinalIgnoreCase);

        return new VolumeMasterStateResponse
        {
            ValuePercent = Math.Clamp(valuePercent, 0, 100),
            IsMuted = isMuted,
        };
    }

    private async Task<VolumeMasterStateResponse> GetMasterVolumeViaPactlAsync(CancellationToken cancellationToken)
    {
        ProcessExecutionResult volumeResult = await _processRunner
            .RunAsync("pactl", ["get-sink-volume", "@DEFAULT_SINK@"], cancellationToken)
            .ConfigureAwait(false);

        if (volumeResult.ExitCode != 0)
        {
            LogMasterVolumeLookupFailed(_logger, volumeResult.StandardError);
            throw new VolumeServiceException(
                StatusCodes.Status501NotImplemented,
                "master_volume_unavailable",
                "Master volume control is unavailable on this server host.");
        }

        ProcessExecutionResult muteResult = await _processRunner
            .RunAsync("pactl", ["get-sink-mute", "@DEFAULT_SINK@"], cancellationToken)
            .ConfigureAwait(false);

        int valuePercent = ParseFirstPercentage(volumeResult.StandardOutput, 0);
        bool isMuted = muteResult.ExitCode == 0
            && muteResult.StandardOutput.Contains("yes", StringComparison.OrdinalIgnoreCase);

        return new VolumeMasterStateResponse
        {
            ValuePercent = Math.Clamp(valuePercent, 0, 100),
            IsMuted = isMuted,
        };
    }

    private async Task<(VolumeMasterStateResponse Master, string? SupportMessage)> GetMasterVolumeBestEffortAsync(CancellationToken cancellationToken)
    {
        try
        {
            VolumeMasterStateResponse masterViaPactl = await GetMasterVolumeViaPactlAsync(cancellationToken).ConfigureAwait(false);
            return (masterViaPactl, null);
        }
        catch (VolumeServiceException)
        {
            try
            {
                VolumeMasterStateResponse masterViaAmixer = await GetMasterVolumeViaAmixerAsync(cancellationToken).ConfigureAwait(false);
                return (masterViaAmixer, null);
            }
            catch (VolumeServiceException)
            {
                return (
                    new VolumeMasterStateResponse
                    {
                        ValuePercent = 0,
                        IsMuted = false,
                    },
                    "Master volume control is unavailable on this server host.");
            }
        }
    }

    private async Task<IReadOnlyList<VolumeSessionResponse>> ListSessionsAsync(CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _processRunner
            .RunAsync("pactl", ["list", "sink-inputs"], cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            LogSessionListingFailed(_logger, result.StandardError);
            throw new VolumeServiceException(
                StatusCodes.Status500InternalServerError,
                "session_list_failed",
                "Failed to enumerate per-application volume sessions.");
        }

        List<VolumeSessionResponse> sessions = ParseSessions(result.StandardOutput);
        return sessions
            .OrderBy(session => session.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<VolumeSessionResponse> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        IReadOnlyList<VolumeSessionResponse> sessions = await ListSessionsAsync(cancellationToken).ConfigureAwait(false);
        VolumeSessionResponse? session = sessions.FirstOrDefault(candidate => string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal));
        if (session is null)
        {
            throw new VolumeServiceException(StatusCodes.Status404NotFound, "session_not_found", $"Volume session '{sessionId}' was not found.");
        }

        return session;
    }

    private async Task<bool> IsPactlAvailableAsync(CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _processRunner
            .RunAsync("pactl", ["info"], cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    private static List<VolumeSessionResponse> ParseSessions(string output)
    {
        List<VolumeSessionResponse> sessions = [];
        SessionParseState? current = null;
        bool inPropertyList = false;

        foreach (string rawLine in output.Split('\n', StringSplitOptions.None))
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith("Sink Input #", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    sessions.Add(current.ToResponse());
                }

                current = new SessionParseState(trimmed["Sink Input #".Length..].Trim());
                inPropertyList = false;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (trimmed.Equals("Properties:", StringComparison.Ordinal)
                || trimmed.Equals("Property List:", StringComparison.Ordinal))
            {
                inPropertyList = true;
                continue;
            }

            if (trimmed.StartsWith("Volume:", StringComparison.Ordinal))
            {
                current.ValuePercent = ParseFirstPercentage(trimmed, current.ValuePercent);
                inPropertyList = false;
                continue;
            }

            if (trimmed.StartsWith("Mute:", StringComparison.Ordinal))
            {
                current.IsMuted = trimmed.EndsWith("yes", StringComparison.OrdinalIgnoreCase);
                inPropertyList = false;
                continue;
            }

            if (inPropertyList)
            {
                int equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                string key = trimmed[..equalsIndex].Trim();
                string value = TrimQuoted(trimmed[(equalsIndex + 1)..].Trim());
                switch (key)
                {
                    case "application.name":
                        current.ApplicationName ??= value;
                        break;
                    case "media.name":
                        current.MediaName ??= value;
                        break;
                    case "application.process.binary":
                        current.ProcessBinary ??= value;
                        break;
                    case "application.process.id":
                        if (int.TryParse(value, out int processId))
                        {
                            current.ProcessId = processId;
                        }

                        break;
                }
            }
        }

        if (current is not null)
        {
            sessions.Add(current.ToResponse());
        }

        return sessions;
    }

    private static int ParseFirstPercentage(string text, int fallback)
    {
        Match match = PercentageRegex.Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out int valuePercent)
            ? Math.Clamp(valuePercent, 0, 100)
            : fallback;
    }

    private static string TrimQuoted(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1];
        }

        return value;
    }

    private static bool ContainsNoSuchEntity(ProcessExecutionResult result)
    {
        return result.StandardError.Contains("No such entity", StringComparison.OrdinalIgnoreCase)
               || result.StandardError.Contains("No such file", StringComparison.OrdinalIgnoreCase)
               || result.StandardOutput.Contains("No such entity", StringComparison.OrdinalIgnoreCase);
    }

    private static string? CombineSupportMessages(string? primary, string secondary)
    {
        if (string.IsNullOrWhiteSpace(primary))
        {
            return secondary;
        }

        if (primary.Contains(secondary, StringComparison.Ordinal))
        {
            return primary;
        }

        return $"{primary} {secondary}";
    }

    private static void EnsureValidVolume(int valuePercent)
    {
        if (valuePercent < 0 || valuePercent > 100)
        {
            throw new VolumeServiceException(StatusCodes.Status400BadRequest, "validation_error", "valuePercent must be between 0 and 100.");
        }
    }

    private void EnsureLinux()
    {
        if (!IsSupportedPlatform)
        {
            throw new VolumeServiceException(
                StatusCodes.Status501NotImplemented,
                "unsupported_platform",
                "Volume control is only supported on Linux.");
        }
    }

    private void NotifyChanged()
    {
        TaskCompletionSource<long> previous;
        long nextStamp = Interlocked.Increment(ref _changeStamp);
        lock (_changeSync)
        {
            previous = _changeSignal;
            _changeSignal = CreateChangeSignal();
        }

        previous.TrySetResult(nextStamp);
    }

    private static TaskCompletionSource<long> CreateChangeSignal()
    {
        return new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [LoggerMessage(
        EventId = 7201,
        Level = LogLevel.Warning,
        Message = "Master volume update failed. stderr={StdErr}")]
    private static partial void LogMasterVolumeUpdateFailed(ILogger logger, string stdErr);

    [LoggerMessage(
        EventId = 7202,
        Level = LogLevel.Warning,
        Message = "Per-application volume update failed. sessionId={SessionId}, stderr={StdErr}")]
    private static partial void LogSessionVolumeUpdateFailed(ILogger logger, string sessionId, string stdErr);

    [LoggerMessage(
        EventId = 7203,
        Level = LogLevel.Warning,
        Message = "Master volume lookup failed. stderr={StdErr}")]
    private static partial void LogMasterVolumeLookupFailed(ILogger logger, string stdErr);

    [LoggerMessage(
        EventId = 7204,
        Level = LogLevel.Warning,
        Message = "Per-application session listing failed. stderr={StdErr}")]
    private static partial void LogSessionListingFailed(ILogger logger, string stdErr);

    internal interface IVolumeProcessRunner
    {
        Task<ProcessExecutionResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
    }

    internal sealed record ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError);

    internal sealed class DefaultVolumeProcessRunner : IVolumeProcessRunner
    {
        public async Task<ProcessExecutionResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                return new ProcessExecutionResult(-1, string.Empty, $"Failed to start '{fileName}'.");
            }

            Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string standardOutput = await stdOutTask.ConfigureAwait(false);
            string standardError = await stdErrTask.ConfigureAwait(false);
            return new ProcessExecutionResult(process.ExitCode, standardOutput, standardError);
        }
    }

    private sealed class SessionParseState(string sessionId)
    {
        public string SessionId { get; } = sessionId;

        public string? ApplicationName { get; set; }

        public string? MediaName { get; set; }

        public string? ProcessBinary { get; set; }

        public int? ProcessId { get; set; }

        public int ValuePercent { get; set; }

        public bool IsMuted { get; set; }

        public VolumeSessionResponse ToResponse()
        {
            string name = FirstNonEmpty(MediaName, ApplicationName, ProcessBinary) ?? $"Session {SessionId}";
            return new VolumeSessionResponse
            {
                SessionId = SessionId,
                Name = name,
                ProcessId = ProcessId,
                ApplicationName = ApplicationName,
                ValuePercent = ValuePercent,
                IsMuted = IsMuted,
            };
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }
    }
}