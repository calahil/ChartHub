using System.CommandLine;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using ChartHub.TranscriptionRunner;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ──────────────────────────────────────────────────────────────────────────
// CLI root
// ──────────────────────────────────────────────────────────────────────────

Option<string> serverOption = new("--server", "ChartHub server base URL (e.g. https://mycharthub.internal)");
serverOption.IsRequired = true;

// ---------- register ----------
Command registerCmd = new("register", "Register this machine as a transcription runner.");
Option<string> tokenOption = new("--token", "One-time registration token issued by the server.");
tokenOption.IsRequired = true;
Option<string> nameOption = new("--name", "Human-readable runner name.");
nameOption.IsRequired = true;
Option<int> concurrencyOption = new("--concurrency", getDefaultValue: () => 1, "Max concurrent transcription jobs.");
registerCmd.AddOption(serverOption);
registerCmd.AddOption(tokenOption);
registerCmd.AddOption(nameOption);
registerCmd.AddOption(concurrencyOption);

registerCmd.SetHandler(async (string server, string token, string name, int concurrency) =>
{
    using ILoggerFactory logFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
    ILogger logger = logFactory.CreateLogger("register");

    RegistrationLog.Registering(logger, server);

    string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    using HttpClient http = new() { BaseAddress = new Uri(server.TrimEnd('/') + '/') };

    HttpResponseMessage resp = await http.PostAsJsonAsync("api/v1/runner/register", new
    {
        runnerName = name,
        registrationToken = token,
        secret,
        maxConcurrency = concurrency,
    }).ConfigureAwait(false);

    if (!resp.IsSuccessStatusCode)
    {
        string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        RegistrationLog.Failed(logger, (int)resp.StatusCode, body);
        throw new InvalidOperationException($"Registration failed ({(int)resp.StatusCode}): {body}");
    }

    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
    string runnerId = doc.RootElement.GetProperty("runnerId").GetString()!;

    RunnerConfig config = new(server.TrimEnd('/'), runnerId, secret, concurrency);
    string configPath = RunnerConfig.DefaultPath;
    Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
    File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

    RegistrationLog.Succeeded(logger, runnerId, configPath);
}, serverOption, tokenOption, nameOption, concurrencyOption);

// ---------- run ----------
Command runCmd = new("run", "Start the runner worker loop.");
Option<string?> configOption = new("--config", getDefaultValue: () => null, "Path to runner config file (default: ~/.charthub-runner/config.json).");
runCmd.AddOption(configOption);

runCmd.SetHandler(async (string? configPath) =>
{
    string resolvedConfig = configPath ?? RunnerConfig.DefaultPath;
    if (!File.Exists(resolvedConfig))
    {
        using ILoggerFactory logFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
        ILogger startLogger = logFactory.CreateLogger("run");
        RegistrationLog.ConfigNotFound(startLogger, resolvedConfig);
        throw new FileNotFoundException($"Config not found at '{resolvedConfig}'. Run 'register' first.");
    }

    RunnerConfig cfg = JsonSerializer.Deserialize<RunnerConfig>(File.ReadAllText(resolvedConfig))
        ?? throw new InvalidOperationException("Failed to parse runner config.");

    IHost host = Host.CreateDefaultBuilder()
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss ");
        })
        .ConfigureServices(services =>
        {
            services.AddSingleton(cfg);
            services.AddHttpClient<IRunnerClient, RunnerClient>((_, c) =>
            {
                c.BaseAddress = new Uri(cfg.ServerUrl.TrimEnd('/') + '/');
                c.Timeout = TimeSpan.FromMinutes(10);
            });
            services.AddHostedService<RunnerWorker>();
        })
        .Build();

    await host.RunAsync().ConfigureAwait(false);
}, configOption);

// ──────────────────────────────────────────────────────────────────────────
// Root command wiring
// ──────────────────────────────────────────────────────────────────────────

RootCommand root = new("ChartHub transcription runner — AI drum generation agent.");
root.AddCommand(registerCmd);
root.AddCommand(runCmd);

return await root.InvokeAsync(args);

namespace ChartHub.TranscriptionRunner
{
    // ──────────────────────────────────────────────────────────────────────────
    // Startup logging helpers
    // ──────────────────────────────────────────────────────────────────────────

    internal static partial class RegistrationLog
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Registering with {Server} ...")]
        public static partial void Registering(ILogger logger, string server);

        [LoggerMessage(Level = LogLevel.Error, Message = "Registration failed ({StatusCode}): {Body}")]
        public static partial void Failed(ILogger logger, int statusCode, string body);

        [LoggerMessage(Level = LogLevel.Information, Message = "Registered as runner '{RunnerId}'. Config saved to: {ConfigPath}")]
        public static partial void Succeeded(ILogger logger, string runnerId, string configPath);

        [LoggerMessage(Level = LogLevel.Error, Message = "Config not found at '{ConfigPath}'. Run 'register' first.")]
        public static partial void ConfigNotFound(ILogger logger, string configPath);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Config model
    // ──────────────────────────────────────────────────────────────────────────

    public sealed record RunnerConfig(
        string ServerUrl,
        string RunnerId,
        string Secret,
        int MaxConcurrency)
    {
        public static readonly string DefaultPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".charthub-runner", "config.json");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Server API client
    // ──────────────────────────────────────────────────────────────────────────

    public interface IRunnerClient
    {
        Task SendHeartbeatAsync(int activeCount, CancellationToken ct);
        Task<ClaimedJob?> ClaimNextJobAsync(TimeSpan? waitTimeout, CancellationToken ct);
        Task MarkProcessingAsync(string jobId, CancellationToken ct);
        Task<string> GetAudioSignedUrlAsync(string jobId, CancellationToken ct);
        Task<byte[]> DownloadAudioAsync(string url, CancellationToken ct);
        Task CompleteJobAsync(string jobId, byte[] midiBytes, CancellationToken ct);
        Task YieldJobAsync(string jobId, CancellationToken ct);
        Task FailJobAsync(string jobId, string reason, CancellationToken ct);
    }

    public sealed record ClaimedJob(
        string JobId,
        string SongId,
        string SongFolderPath,
        string Aggressiveness,
        int AttemptNumber);

    public sealed class RunnerClient : IRunnerClient
    {
        private readonly HttpClient _http;

        public RunnerClient(HttpClient http, RunnerConfig cfg)
        {
            _http = http;
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Runner", $"{cfg.RunnerId}:{cfg.Secret}");
        }

        public async Task SendHeartbeatAsync(int activeCount, CancellationToken ct)
        {
            await _http.PostAsJsonAsync("api/v1/runner/heartbeat", new { activeJobCount = activeCount }, ct)
                .ConfigureAwait(false);
        }

        public async Task<ClaimedJob?> ClaimNextJobAsync(TimeSpan? waitTimeout, CancellationToken ct)
        {
            string path = "api/v1/runner/jobs/claim";
            if (waitTimeout is { } configuredWait && configuredWait > TimeSpan.Zero)
            {
                int waitMs = (int)Math.Clamp(configuredWait.TotalMilliseconds, 1, 60_000);
                path = $"{path}?waitMs={waitMs}";
            }

            HttpResponseMessage resp = await _http.PostAsync(path, content: null, ct)
                .ConfigureAwait(false);

            if (resp.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return null;
            }

            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            JsonElement root = doc.RootElement;
            return new ClaimedJob(
                JobId: root.GetProperty("jobId").GetString()!,
                SongId: root.GetProperty("songId").GetString()!,
                SongFolderPath: root.GetProperty("songFolderPath").GetString()!,
                Aggressiveness: root.GetProperty("aggressiveness").GetString()!,
                AttemptNumber: root.GetProperty("attemptNumber").GetInt32());
        }

        public async Task MarkProcessingAsync(string jobId, CancellationToken ct)
        {
            await _http.PostAsync($"api/v1/runner/jobs/{jobId}/processing", content: null, ct)
                .ConfigureAwait(false);
        }

        public async Task<string> GetAudioSignedUrlAsync(string jobId, CancellationToken ct)
        {
            HttpResponseMessage resp = await _http.PostAsync($"api/v1/runner/jobs/{jobId}/audio-url", content: null, ct)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return doc.RootElement.GetProperty("url").GetString()!;
        }

        public async Task<byte[]> DownloadAudioAsync(string url, CancellationToken ct)
        {
            HttpResponseMessage resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }

        public async Task CompleteJobAsync(string jobId, byte[] midiBytes, CancellationToken ct)
        {
            using MultipartFormDataContent form = new();
            using ByteArrayContent midiContent = new(midiBytes);
            midiContent.Headers.ContentType = new MediaTypeHeaderValue("audio/midi");
            form.Add(midiContent, "midi", "result.mid");

            HttpResponseMessage resp = await _http.PostAsync($"api/v1/runner/jobs/{jobId}/complete", form, ct)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
        }

        public async Task YieldJobAsync(string jobId, CancellationToken ct)
        {
            await _http.PostAsync($"api/v1/runner/jobs/{jobId}/yield", content: null, ct)
                .ConfigureAwait(false);
        }

        public async Task FailJobAsync(string jobId, string reason, CancellationToken ct)
        {
            await _http.PostAsJsonAsync($"api/v1/runner/jobs/{jobId}/fail", new { reason }, ct)
                .ConfigureAwait(false);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Worker hosted service
    // ──────────────────────────────────────────────────────────────────────────

    public sealed class RunnerWorker : BackgroundService
    {
        private readonly IRunnerClient _client;
        private readonly RunnerConfig _cfg;
        private readonly ILogger<RunnerWorker> _logger;

        private static readonly TimeSpan IdleBackoffMin = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan IdleBackoffMax = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ErrorBackoffMin = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ErrorBackoffMax = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ClaimLongPollTimeout = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan BusyHeartbeatInterval = TimeSpan.FromSeconds(25);
        private static readonly TimeSpan IdleHeartbeatInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan MaxBurstDelay = TimeSpan.FromSeconds(1);

        private const int BurstFastClaimsAfterJob = 3;
        private const double BackoffJitterRatio = 0.20;
        private const int ClaimErrorWarnEvery = 10;

        public RunnerWorker(IRunnerClient client, RunnerConfig cfg, ILogger<RunnerWorker> logger)
        {
            _client = client;
            _cfg = cfg;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            RunnerLog.Starting(_logger, _cfg.RunnerId, _cfg.MaxConcurrency);

            DateTimeOffset lastHeartbeat = DateTimeOffset.MinValue;
            int activeJobs = 0;
            int previousActiveJobs = -1;
            int idleNoJobStreak = 0;
            int claimErrorStreak = 0;
            int burstClaimsRemaining = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                int activeSnapshot = Volatile.Read(ref activeJobs);
                if (activeSnapshot != previousActiveJobs)
                {
                    previousActiveJobs = activeSnapshot;
                    lastHeartbeat = DateTimeOffset.MinValue;
                }

                TimeSpan heartbeatInterval = activeSnapshot > 0 ? BusyHeartbeatInterval : IdleHeartbeatInterval;
                if (DateTimeOffset.UtcNow - lastHeartbeat >= heartbeatInterval)
                {
                    try
                    {
                        await _client.SendHeartbeatAsync(activeSnapshot, stoppingToken).ConfigureAwait(false);
                        lastHeartbeat = DateTimeOffset.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        RunnerLog.HeartbeatFailed(_logger, ex);
                    }
                }

                if (activeSnapshot < _cfg.MaxConcurrency)
                {
                    ClaimedJob? job = null;
                    try
                    {
                        job = await _client.ClaimNextJobAsync(ClaimLongPollTimeout, stoppingToken).ConfigureAwait(false);
                        claimErrorStreak = 0;
                    }
                    catch (Exception ex)
                    {
                        claimErrorStreak++;
                        TimeSpan claimErrorDelay = ComputeBackoffWithJitter(claimErrorStreak, ErrorBackoffMin, ErrorBackoffMax);
                        if (claimErrorStreak == 1 || claimErrorStreak % ClaimErrorWarnEvery == 0)
                        {
                            RunnerLog.ClaimFailed(_logger, claimErrorStreak, claimErrorDelay.TotalMilliseconds, ex);
                        }
                        else
                        {
                            RunnerLog.ClaimFailedDebug(_logger, claimErrorStreak, claimErrorDelay.TotalMilliseconds);
                        }

                        await Task.Delay(claimErrorDelay, stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    if (job is not null)
                    {
                        Interlocked.Increment(ref activeJobs);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ProcessJobAsync(job, stoppingToken).ConfigureAwait(false);
                            }
                            finally
                            {
                                Interlocked.Decrement(ref activeJobs);
                            }
                        }, stoppingToken);

                        idleNoJobStreak = 0;
                        burstClaimsRemaining = BurstFastClaimsAfterJob;
                        continue;
                    }

                    idleNoJobStreak++;
                    if (burstClaimsRemaining > 0)
                    {
                        burstClaimsRemaining--;
                        var burstDelay = TimeSpan.FromMilliseconds(Random.Shared.NextInt64(0, (long)MaxBurstDelay.TotalMilliseconds + 1));
                        await Task.Delay(burstDelay, stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    TimeSpan idleDelay = ComputeBackoffWithJitter(idleNoJobStreak, IdleBackoffMin, IdleBackoffMax);
                    RunnerLog.IdleBackoff(_logger, idleNoJobStreak, idleDelay.TotalMilliseconds);
                    await Task.Delay(idleDelay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }

            RunnerLog.ShuttingDown(_logger);
        }

        private static TimeSpan ComputeBackoffWithJitter(int streak, TimeSpan minDelay, TimeSpan maxDelay)
        {
            int exponent = Math.Max(0, streak - 1);
            double delayMs = minDelay.TotalMilliseconds * Math.Pow(2, exponent);
            delayMs = Math.Min(delayMs, maxDelay.TotalMilliseconds);

            double jitterScale = 1 + ((Random.Shared.NextDouble() * 2 - 1) * BackoffJitterRatio);
            double jitteredDelay = Math.Clamp(delayMs * jitterScale, minDelay.TotalMilliseconds, maxDelay.TotalMilliseconds);
            return TimeSpan.FromMilliseconds(jitteredDelay);
        }

        private async Task ProcessJobAsync(ClaimedJob job, CancellationToken ct)
        {
            RunnerLog.ProcessingJob(_logger, job.JobId, job.Aggressiveness);

            string workDir = Path.Combine(Path.GetTempPath(), "charthub-runner", job.JobId);
            Directory.CreateDirectory(workDir);

            try
            {
                await _client.MarkProcessingAsync(job.JobId, ct).ConfigureAwait(false);

                string audioUrl = await _client.GetAudioSignedUrlAsync(job.JobId, ct).ConfigureAwait(false);
                byte[] audioBytes = await _client.DownloadAudioAsync(audioUrl, ct).ConfigureAwait(false);
                string audioPath = Path.Combine(workDir, "audio.ogg");
                await File.WriteAllBytesAsync(audioPath, audioBytes, ct).ConfigureAwait(false);

                string midiPath = await RunBasicPitchAsync(audioPath, workDir, job.Aggressiveness, ct).ConfigureAwait(false);

                byte[] midiBytes = await File.ReadAllBytesAsync(midiPath, ct).ConfigureAwait(false);
                await _client.CompleteJobAsync(job.JobId, midiBytes, ct).ConfigureAwait(false);

                RunnerLog.JobComplete(_logger, job.JobId);
            }
            catch (OperationCanceledException)
            {
                RunnerLog.JobYielded(_logger, job.JobId);
                try { await _client.YieldJobAsync(job.JobId, CancellationToken.None).ConfigureAwait(false); } catch { }
            }
            catch (Exception ex)
            {
                RunnerLog.JobFailed(_logger, job.JobId, ex);
                try { await _client.FailJobAsync(job.JobId, ex.Message, CancellationToken.None).ConfigureAwait(false); } catch { }
            }
            finally
            {
                TryDeleteDirectory(workDir);
            }
        }

        private static async Task<string> RunBasicPitchAsync(
            string audioPath,
            string workDir,
            string aggressiveness,
            CancellationToken ct)
        {
            (float onset, float frame, int minLen) = aggressiveness.ToLowerInvariant() switch
            {
                "high" => (0.30f, 0.10f, 5),
                "low" => (0.70f, 0.50f, 11),
                _ => (0.50f, 0.30f, 7),
            };

            string outputDir = Path.Combine(workDir, "bp-out");
            Directory.CreateDirectory(outputDir);

            System.Diagnostics.ProcessStartInfo psi = new("basic-pitch")
            {
                ArgumentList =
                {
                    "--onset-threshold", onset.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    "--frame-threshold", frame.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    "--minimum-note-length", minLen.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--midi-tempo", "120",
                    "--save-midi",
                    "--sonify-midi",
                    outputDir,
                    audioPath,
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start basic-pitch process.");

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                string err = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"basic-pitch exited with code {process.ExitCode}: {err}");
            }

            string[] midiFiles = Directory.GetFiles(outputDir, "*.mid");
            if (midiFiles.Length == 0)
            {
                throw new InvalidOperationException("basic-pitch produced no MIDI output.");
            }

            return midiFiles[0];
        }

        private static void TryDeleteDirectory(string path)
        {
            try { Directory.Delete(path, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Structured log messages
    // ──────────────────────────────────────────────────────────────────────────

    internal static partial class RunnerLog
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Runner {RunnerId} starting. MaxConcurrency={Max}")]
        public static partial void Starting(ILogger logger, string runnerId, int max);

        [LoggerMessage(Level = LogLevel.Information, Message = "Runner shutting down.")]
        public static partial void ShuttingDown(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Heartbeat failed.")]
        public static partial void HeartbeatFailed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Claim failed (streak={Streak}, retryDelayMs={RetryDelayMs}).")]
        public static partial void ClaimFailed(ILogger logger, int streak, double retryDelayMs, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Claim failure streak={Streak}; backing off for {RetryDelayMs}ms.")]
        public static partial void ClaimFailedDebug(ILogger logger, int streak, double retryDelayMs);

        [LoggerMessage(Level = LogLevel.Debug, Message = "No jobs available (streak={Streak}); next claim in {DelayMs}ms.")]
        public static partial void IdleBackoff(ILogger logger, int streak, double delayMs);

        [LoggerMessage(Level = LogLevel.Information, Message = "Processing job {JobId} ({Aggressiveness})")]
        public static partial void ProcessingJob(ILogger logger, string jobId, string aggressiveness);

        [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} complete.")]
        public static partial void JobComplete(ILogger logger, string jobId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} yielded (shutdown).")]
        public static partial void JobYielded(ILogger logger, string jobId);

        [LoggerMessage(Level = LogLevel.Error, Message = "Job {JobId} failed.")]
        public static partial void JobFailed(ILogger logger, string jobId, Exception exception);
    }
}
