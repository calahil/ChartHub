using System.Diagnostics;
using System.Text;
using ChartHub.Utilities;
using YamlDotNet.RepresentationModel;

namespace ChartHub.Services
{
    public interface IOnyxPipelineService
    {
        Task<OnyxInstallResult> InstallAsync(
            string songPath,
            string sourceSuffix,
            IProgress<InstallProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default);
    }

    public sealed class OnyxService : IOnyxPipelineService
    {
        private static readonly string[] OnyxSearchRoots =
        [
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
        ];

        private readonly AppGlobalSettings globalSettings;
        private readonly Func<string[], CancellationToken, Task>? _runOnyxOverride;

        public OnyxService(AppGlobalSettings settings, string songPath)
            : this(settings)
        {
        }

        public OnyxService(AppGlobalSettings settings)
        {
            globalSettings = settings;
        }

        internal OnyxService(AppGlobalSettings settings, Func<string[], CancellationToken, Task> runOnyxOverride)
        {
            globalSettings = settings;
            _runOnyxOverride = runOnyxOverride;
        }

        public async Task<OnyxInstallResult> InstallAsync(
            string songPath,
            string sourceSuffix,
            IProgress<InstallProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var pipelineStopwatch = Stopwatch.StartNew();
            var jobId = Guid.NewGuid().ToString("N");
            var stagingRoot = Path.Combine(globalSettings.StagingDir, "onyx", jobId);
            var importRoot = Path.Combine(stagingRoot, "import");
            var importPath = Path.Combine(importRoot, SafePathHelper.SanitizeFileName(Path.GetFileNameWithoutExtension(songPath), "import"));
            var buildOutputRoot = Path.Combine(globalSettings.OutputDir, "onyx", jobId);

            Logger.LogInfo("Onyx", "Onyx pipeline started", new Dictionary<string, object?>
            {
                ["songPath"] = songPath,
                ["stagingRoot"] = stagingRoot,
                ["buildOutputRoot"] = buildOutputRoot,
            });

            Directory.CreateDirectory(importRoot);
            Directory.CreateDirectory(buildOutputRoot);

            ReportProgress(progress, InstallStage.Preparing, "Preparing Onyx workspace", 0, songPath);

            try
            {
                ReportProgress(progress, InstallStage.Importing, "Running Onyx import", 15, songPath, isIndeterminate: true);
                await ExecuteCommandAsync(
                    InstallStage.Importing,
                    ["import", songPath, "--to", importPath],
                    progress,
                    cancellationToken);

                ReportProgress(progress, InstallStage.ValidatingImport, "Validating imported project", 35, songPath);
                var songYamlPath = Directory.GetFiles(importPath, "song.yml", SearchOption.AllDirectories).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(songYamlPath))
                {
                    Logger.LogWarning("Onyx", "Onyx import completed but song.yml was not generated", new Dictionary<string, object?>
                    {
                        ["importPath"] = importPath,
                        ["elapsedMs"] = pipelineStopwatch.ElapsedMilliseconds,
                    });
                    throw new InvalidOperationException("Onyx import completed but song.yml was not generated.");
                }

                ReportProgress(progress, InstallStage.PatchingYaml, "Patching song.yml targets", 45, songPath);
                var processedMetadata = ProcessYaml(songYamlPath, sourceSuffix);

                ReportProgress(progress, InstallStage.Building, "Running Onyx build", 60, songPath, isIndeterminate: true);
                await ExecuteCommandAsync(
                    InstallStage.Building,
                    ["build", songYamlPath, "--target", "ps", "--to", buildOutputRoot],
                    progress,
                    cancellationToken);

                ReportProgress(progress, InstallStage.MovingToCloneHero, "Moving built output into Clone Hero songs", 85, songPath);
                var finalDirectory = MoveBuiltOutputToCloneHero(buildOutputRoot, processedMetadata.FinalDirectoryName);

                ReportProgress(progress, InstallStage.CleaningUp, "Cleaning temporary Onyx workspace", 95, songPath);
                CleanupDirectory(stagingRoot);
                CleanupDirectory(buildOutputRoot);

                Logger.LogInfo("Onyx", "Onyx pipeline completed", new Dictionary<string, object?>
                {
                    ["outputDir"] = finalDirectory,
                    ["elapsedMs"] = pipelineStopwatch.ElapsedMilliseconds,
                });

                ReportProgress(progress, InstallStage.Completed, "Onyx install completed", 100, songPath);
                return new OnyxInstallResult(finalDirectory, importPath, buildOutputRoot, processedMetadata.Metadata);
            }
            catch (OperationCanceledException)
            {
                CleanupDirectory(stagingRoot);
                CleanupDirectory(buildOutputRoot);
                Logger.LogInfo("Onyx", "Onyx pipeline cancelled", new Dictionary<string, object?>
                {
                    ["songPath"] = songPath,
                    ["elapsedMs"] = pipelineStopwatch.ElapsedMilliseconds,
                });
                ReportProgress(progress, InstallStage.Cancelled, "Onyx install cancelled", null, songPath);
                throw;
            }
            catch (Exception ex)
            {
                CleanupDirectory(stagingRoot);
                CleanupDirectory(buildOutputRoot);
                Logger.LogError("Onyx", "Onyx pipeline failed", ex, new Dictionary<string, object?>
                {
                    ["songPath"] = songPath,
                    ["outputDir"] = buildOutputRoot,
                    ["elapsedMs"] = pipelineStopwatch.ElapsedMilliseconds,
                });
                ReportProgress(progress, InstallStage.Failed, $"Onyx install failed: {ex.Message}", null, songPath, ex.Message);
                throw;
            }
        }

        private async Task ExecuteCommandAsync(
            InstallStage stage,
            string[] arguments,
            IProgress<InstallProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            if (_runOnyxOverride is not null)
            {
                await _runOnyxOverride(arguments, cancellationToken);
                return;
            }

            await RunOnyxAsync(stage, arguments, progress, cancellationToken);
        }

        private static async Task RunOnyxAsync(
            InstallStage stage,
            string[] arguments,
            IProgress<InstallProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            var phase = arguments.Length > 0 ? arguments[0] : "unknown";
            var stopwatch = Stopwatch.StartNew();
            var executablePath = ResolveOnyxExecutablePath();
            Logger.LogInfo("Onyx", "Onyx command started", new Dictionary<string, object?>
            {
                ["phase"] = phase,
                ["executablePath"] = executablePath,
                ["argumentCount"] = arguments.Length,
            });

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start the Onyx process.");

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            });

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var readStdoutTask = ReadOutputAsync(process.StandardOutput, false, stdout, stage, progress, cancellationToken);
            var readStderrTask = ReadOutputAsync(process.StandardError, true, stderr, stage, progress, cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(readStdoutTask, readStderrTask);

            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(stderr.ToString())
                    ? stdout.ToString()
                    : stderr.ToString();

                Logger.LogError("Onyx", "Onyx command failed", new InvalidOperationException(message.Trim()), new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["exitCode"] = process.ExitCode,
                    ["elapsedMs"] = stopwatch.ElapsedMilliseconds,
                });
                throw new InvalidOperationException($"Onyx exited with code {process.ExitCode}: {message}".Trim());
            }

            Logger.LogInfo("Onyx", "Onyx command completed", new Dictionary<string, object?>
            {
                ["phase"] = phase,
                ["elapsedMs"] = stopwatch.ElapsedMilliseconds,
            });
        }

        private static async Task ReadOutputAsync(
            StreamReader reader,
            bool isError,
            StringBuilder buffer,
            InstallStage stage,
            IProgress<InstallProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;

                buffer.AppendLine(line);
                progress?.Report(new InstallProgressUpdate(
                    Stage: stage,
                    Message: isError ? "Onyx error output" : "Onyx output",
                    ProgressPercent: null,
                    LogLine: line,
                    IsIndeterminate: true));
            }
        }

        private static string ResolveOnyxExecutablePath()
        {
            foreach (var root in OnyxSearchRoots.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                var candidate = Path.GetFullPath(Path.Combine(root, "tools", "onyx"));
                if (File.Exists(candidate))
                    return candidate;
            }

            throw new FileNotFoundException("Unable to locate the Onyx executable in a trusted tools directory.");
        }

        private (string FinalDirectoryName, SongMetadata Metadata) ProcessYaml(string songPath, string sourceSuffix)
        {
            var yaml = new YamlStream();
            using (var reader = new StreamReader(songPath))
            {
                yaml.Load(reader);
            }
            var root = (YamlMappingNode)yaml.Documents[0].RootNode;
            if (!root.Children.TryGetValue(new YamlScalarNode("targets"), out var targetsNode)
                || targetsNode is not YamlMappingNode targets)
            {
                targets = new YamlMappingNode();
                root.Children[new YamlScalarNode("targets")] = targets;
            }

            var psNode = new YamlMappingNode
            {
                { "game", "ps" }
            };

            if (!targets.Children.ContainsKey(new YamlScalarNode("ps")))
            {
                targets.Add("ps", psNode);
            }
            else
            {
                targets.Children[new YamlScalarNode("ps")] = psNode;
            }

            var metadata = root.Children.TryGetValue(new YamlScalarNode("metadata"), out var metadataNode)
                && metadataNode is YamlMappingNode metadataMapping
                ? metadataMapping
                : new YamlMappingNode();

            var artist = GetYamlScalar(metadata, "artist", "Unknown Artist");
            var title = GetYamlScalar(metadata, "title", Path.GetFileNameWithoutExtension(songPath));
            var charter = GetYamlScalar(metadata, "charter", "Unknown Charter");
            var normalizedSuffix = NormalizeSourceSuffix(sourceSuffix);
            var finalArtist = SafePathHelper.SanitizeFileName(artist, "song");
            var finalTitle = SafePathHelper.SanitizeFileName(title, "song");
            var finalCharter = SafePathHelper.SanitizeFileName(charter, "song");
            var finalDirectoryName = Path.Combine(finalArtist, finalTitle, $"{finalCharter}__{normalizedSuffix}");
            using var writer = new StreamWriter(songPath);
            yaml.Save(writer, assignAnchors: false);
            return (finalDirectoryName, new SongMetadata(artist, title, charter));
        }

        private string MoveBuiltOutputToCloneHero(string buildOutputRoot, string finalDirectoryName)
        {
            Directory.CreateDirectory(globalSettings.CloneHeroSongsDir);

            var candidateDir = ResolveProducedDirectory(buildOutputRoot);
            var finalDirectory = ResolveUniqueDirectory(Path.Combine(globalSettings.CloneHeroSongsDir, finalDirectoryName));

            if (string.Equals(candidateDir, buildOutputRoot, StringComparison.Ordinal))
            {
                Directory.CreateDirectory(finalDirectory);

                foreach (var directory in Directory.GetDirectories(buildOutputRoot))
                {
                    var destination = Path.Combine(finalDirectory, Path.GetFileName(directory));
                    Directory.Move(directory, destination);
                }

                foreach (var file in Directory.GetFiles(buildOutputRoot))
                {
                    var destination = Path.Combine(finalDirectory, Path.GetFileName(file));
                    File.Move(file, destination, overwrite: true);
                }

                return finalDirectory;
            }

            var finalParent = Path.GetDirectoryName(finalDirectory);
            if (!string.IsNullOrWhiteSpace(finalParent))
                Directory.CreateDirectory(finalParent);

            Directory.Move(candidateDir, finalDirectory);
            return finalDirectory;
        }

        private static string ResolveProducedDirectory(string buildOutputRoot)
        {
            var directories = Directory.GetDirectories(buildOutputRoot);
            var files = Directory.GetFiles(buildOutputRoot);

            if (directories.Length == 1 && files.Length == 0)
                return directories[0];

            return buildOutputRoot;
        }

        private static void CleanupDirectory(string path)
        {
            if (!Directory.Exists(path))
                return;

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Onyx", "Failed to clean temporary directory", new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["error"] = ex.Message,
                });
            }
        }

        private static string GetYamlScalar(YamlMappingNode node, string key, string fallback)
        {
            return node.Children.TryGetValue(new YamlScalarNode(key), out var value)
                ? value.ToString() ?? fallback
                : fallback;
        }

        private static void ReportProgress(
            IProgress<InstallProgressUpdate>? progress,
            InstallStage stage,
            string message,
            double? progressPercent,
            string? currentItemName,
            string? logLine = null,
            bool isIndeterminate = false)
        {
            progress?.Report(new InstallProgressUpdate(stage, message, progressPercent, currentItemName, logLine, isIndeterminate));
        }

        private static string NormalizeSourceSuffix(string? sourceSuffix)
        {
            if (string.IsNullOrWhiteSpace(sourceSuffix))
                return "unknown";

            return sourceSuffix.Trim().ToLowerInvariant();
        }

        private static string ResolveUniqueDirectory(string path)
        {
            if (!Directory.Exists(path))
                return path;

            var parent = Path.GetDirectoryName(path) ?? string.Empty;
            var baseName = Path.GetFileName(path);
            var counter = 2;

            while (true)
            {
                var candidate = Path.Combine(parent, $"{baseName}_{counter}");
                if (!Directory.Exists(candidate))
                    return candidate;

                counter++;
            }
        }
    }
}
