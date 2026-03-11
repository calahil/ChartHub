using System.Diagnostics;
using RhythmVerseClient.Utilities;
using YamlDotNet.RepresentationModel;

namespace RhythmVerseClient.Services
{
    public partial class Onyx
    {
        private string TempPath { get; set; }
        private string DestPath { get; set; }
        private string StagingPath { get; set; }
        private readonly AppGlobalSettings globalSettings;

        public Onyx(AppGlobalSettings settings, string songPath)
        {
            globalSettings = settings;
            TempPath = Path.Combine(globalSettings.TempDir,
                Guid.NewGuid().ToString()
            );
            DestPath = globalSettings.OutputDir;
            StagingPath = globalSettings.StagingDir;
            var importPath = Path.Combine(TempPath, Path.GetFileName(songPath));
            Process.Start(new ProcessStartInfo
            {
                FileName = "./tools/onyx",
                Arguments = $"import \"{songPath}\" --to \"{importPath}\"",
                CreateNoWindow = true,
                UseShellExecute = true
            })?.WaitForExit();

            if (Directory.GetFiles(importPath, "song.yml").Length == 0)
                return;

            var file = Path.Combine(importPath, "song.yml");
            ProcessYaml(file);
            Process.Start(new ProcessStartInfo
            {
                FileName = "./tools/onyx",
                Arguments = $"build \"{file}\" --target ps --to \"{DestPath}\"",
                CreateNoWindow = true,
                UseShellExecute = true
            })?.WaitForExit();
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
