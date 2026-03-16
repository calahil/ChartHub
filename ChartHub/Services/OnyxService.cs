using System.Diagnostics;
using ChartHub.Utilities;
using YamlDotNet.RepresentationModel;

namespace ChartHub.Services
{
    public partial class OnyxService
    {
        private static readonly string[] OnyxSearchRoots =
        [
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
        ];

        private string TempPath { get; set; }
        private string DestPath { get; set; }
        private string StagingPath { get; set; }
        private readonly AppGlobalSettings globalSettings;

        private readonly Action<string[]> _runOnyx;

        public OnyxService(AppGlobalSettings settings, string songPath)
            : this(settings, songPath, static args => RunOnyx(args)) { }

        internal OnyxService(AppGlobalSettings settings, string songPath, Action<string[]> runOnyx)
        {
            var pipelineStopwatch = Stopwatch.StartNew();
            Logger.LogInfo("Onyx", "Onyx pipeline started", new Dictionary<string, object?>
            {
                ["songPath"] = songPath,
            });

            globalSettings = settings;
            _runOnyx = runOnyx;
            TempPath = Path.Combine(globalSettings.TempDir,
                Guid.NewGuid().ToString()
            );
            DestPath = globalSettings.OutputDir;
            StagingPath = globalSettings.StagingDir;
            Directory.CreateDirectory(TempPath);
            var importPath = Path.Combine(TempPath, Path.GetFileName(songPath));
            try
            {
                _runOnyx(["import", songPath, "--to", importPath]);

                if (Directory.GetFiles(importPath, "song.yml").Length == 0)
                {
                    Logger.LogWarning("Onyx", "Onyx import completed but song.yml was not generated", new Dictionary<string, object?>
                    {
                        ["importPath"] = importPath,
                        ["elapsedMs"] = pipelineStopwatch.ElapsedMilliseconds,
                    });
                    return;
                }

                var file = Path.Combine(importPath, "song.yml");
                ProcessYaml(file);
                _runOnyx(["build", file, "--target", "ps", "--to", DestPath]);

                Logger.LogInfo("Onyx", "Onyx pipeline completed", new Dictionary<string, object?>
                {
                    ["outputDir"] = DestPath,
                    ["elapsedMs"] = pipelineStopwatch.ElapsedMilliseconds,
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("Onyx", "Onyx pipeline failed", ex, new Dictionary<string, object?>
                {
                    ["songPath"] = songPath,
                    ["outputDir"] = DestPath,
                    ["elapsedMs"] = pipelineStopwatch.ElapsedMilliseconds,
                });
                throw;
            }
        }

        private static void RunOnyx(params string[] arguments)
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

            process.WaitForExit();
            var standardError = process.StandardError.ReadToEnd();
            var standardOutput = process.StandardOutput.ReadToEnd();
            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(standardError)
                    ? standardOutput
                    : standardError;

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

        private void ProcessYaml(string songPath)
        {
            var yaml = new YamlStream();
            using (var reader = new StreamReader(songPath))
            {
                yaml.Load(reader);
            }
            var root = (YamlMappingNode)yaml.Documents[0].RootNode;
            var targets = (YamlMappingNode)root.Children[new YamlScalarNode("targets")];
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
            // get metadata block
            var metadata = (YamlMappingNode)root.Children[new YamlScalarNode("metadata")];

            // extract values
            var artist = metadata.Children[new YamlScalarNode("artist")].ToString();
            var title = metadata.Children[new YamlScalarNode("title")].ToString();
            var directory = Path.Combine(DestPath, $"{artist} - {title}");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            using var writer = new StreamWriter(songPath);
            yaml.Save(writer, assignAnchors: false);
        }
    }
}
