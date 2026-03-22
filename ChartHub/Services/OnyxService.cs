using System.Diagnostics;
using System.Text;

using ChartHub.Utilities;

using YamlDotNet.RepresentationModel;

namespace ChartHub.Services;

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

    private readonly AppGlobalSettings _globalSettings;
    private readonly Func<string[], CancellationToken, Task>? _runOnyxOverride;

    public OnyxService(AppGlobalSettings settings, string songPath)
        : this(settings)
    {
    }

    public OnyxService(AppGlobalSettings settings)
    {
        _globalSettings = settings;
    }

    internal OnyxService(AppGlobalSettings settings, Func<string[], CancellationToken, Task> runOnyxOverride)
    {
        _globalSettings = settings;
        _runOnyxOverride = runOnyxOverride;
    }

    public async Task<OnyxInstallResult> InstallAsync(
        string songPath,
        string sourceSuffix,
        IProgress<InstallProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pipelineStopwatch = Stopwatch.StartNew();
        string jobId = Guid.NewGuid().ToString("N");
        string stagingRoot = Path.Combine(_globalSettings.StagingDir, "onyx", jobId);
        string importRoot = Path.Combine(stagingRoot, "import");
        string importPath = Path.Combine(importRoot, SafePathHelper.SanitizeFileName(Path.GetFileNameWithoutExtension(songPath), "import"));
        string buildOutputRoot = Path.Combine(_globalSettings.OutputDir, "onyx", jobId);

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
            string? songYamlPath = Directory.GetFiles(importPath, "song.yml", SearchOption.AllDirectories).FirstOrDefault();
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
            (string FinalDirectoryName, SongMetadata Metadata) processedMetadata = ProcessYaml(songYamlPath, sourceSuffix);

            ReportProgress(progress, InstallStage.Building, "Running Onyx build", 60, songPath, isIndeterminate: true);
            await ExecuteCommandAsync(
                InstallStage.Building,
                ["build", songYamlPath, "--target", "ps", "--to", buildOutputRoot],
                progress,
                cancellationToken);

            ReportProgress(progress, InstallStage.MovingToCloneHero, "Moving built output into Clone Hero songs", 85, songPath);
            string finalDirectory = MoveBuiltOutputToCloneHero(buildOutputRoot, processedMetadata.FinalDirectoryName);

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
        string phase = arguments.Length > 0 ? arguments[0] : "unknown";
        var stopwatch = Stopwatch.StartNew();
        string executablePath = ResolveOnyxExecutablePath();
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

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Onyx process.");

        using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        Task readStdoutTask = ReadOutputAsync(process.StandardOutput, false, stdout, stage, progress, cancellationToken);
        Task readStderrTask = ReadOutputAsync(process.StandardError, true, stderr, stage, progress, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(readStdoutTask, readStderrTask);

        if (process.ExitCode != 0)
        {
            string message = string.IsNullOrWhiteSpace(stderr.ToString())
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
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

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
        foreach (string? root in OnyxSearchRoots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            string candidate = Path.GetFullPath(Path.Combine(root, "tools", "onyx"));
            if (File.Exists(candidate))
            {
                return candidate;
            }
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
        if (!root.Children.TryGetValue(new YamlScalarNode("targets"), out YamlNode? targetsNode)
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

        YamlMappingNode metadata = root.Children.TryGetValue(new YamlScalarNode("metadata"), out YamlNode? metadataNode)
            && metadataNode is YamlMappingNode metadataMapping
            ? metadataMapping
            : new YamlMappingNode();

        string artist = GetYamlScalar(metadata, "artist", "Unknown Artist");
        string title = GetYamlScalar(metadata, "title", Path.GetFileNameWithoutExtension(songPath));
        string charter = GetYamlScalar(metadata, "charter", "Unknown Charter");
        string normalizedSuffix = NormalizeSourceSuffix(sourceSuffix);
        string finalArtist = SafePathHelper.SanitizeFileName(artist, "song");
        string finalTitle = SafePathHelper.SanitizeFileName(title, "song");
        string finalCharter = SafePathHelper.SanitizeFileName(charter, "song");
        string finalDirectoryName = Path.Combine(finalArtist, finalTitle, $"{finalCharter}__{normalizedSuffix}");
        using var writer = new StreamWriter(songPath);
        yaml.Save(writer, assignAnchors: false);
        return (finalDirectoryName, new SongMetadata(artist, title, charter));
    }

    private string MoveBuiltOutputToCloneHero(string buildOutputRoot, string finalDirectoryName)
    {
        Directory.CreateDirectory(_globalSettings.CloneHeroSongsDir);

        string candidateDir = ResolveProducedDirectory(buildOutputRoot);
        string finalDirectory = ResolveUniqueDirectory(Path.Combine(_globalSettings.CloneHeroSongsDir, finalDirectoryName));

        if (string.Equals(candidateDir, buildOutputRoot, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(finalDirectory);

            foreach (string directory in Directory.GetDirectories(buildOutputRoot))
            {
                string destination = Path.Combine(finalDirectory, Path.GetFileName(directory));
                Directory.Move(directory, destination);
            }

            foreach (string file in Directory.GetFiles(buildOutputRoot))
            {
                string destination = Path.Combine(finalDirectory, Path.GetFileName(file));
                File.Move(file, destination, overwrite: true);
            }

            return finalDirectory;
        }

        string? finalParent = Path.GetDirectoryName(finalDirectory);
        if (!string.IsNullOrWhiteSpace(finalParent))
        {
            Directory.CreateDirectory(finalParent);
        }

        Directory.Move(candidateDir, finalDirectory);
        return finalDirectory;
    }

    private static string ResolveProducedDirectory(string buildOutputRoot)
    {
        string[] directories = Directory.GetDirectories(buildOutputRoot);
        string[] files = Directory.GetFiles(buildOutputRoot);

        if (directories.Length == 1 && files.Length == 0)
        {
            return directories[0];
        }

        return buildOutputRoot;
    }

    private static void CleanupDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

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
        return node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value)
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
        {
            return "unknown";
        }

        return sourceSuffix.Trim().ToLowerInvariant();
    }

    private static string ResolveUniqueDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return path;
        }

        string parent = Path.GetDirectoryName(path) ?? string.Empty;
        string baseName = Path.GetFileName(path);
        int counter = 2;

        while (true)
        {
            string candidate = Path.Combine(parent, $"{baseName}_{counter}");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }

            counter++;
        }
    }
}
