using System.Diagnostics;
using YamlDotNet.RepresentationModel;

namespace RhythmVerseClient.Services
{
    public partial class Onyx
    {
        private string TempPath { get; set; }
        private string DestPath { get; set; }

        public Onyx(List<string> songPaths)
        {
            TempPath = Path.Combine(
                Path.GetTempPath(),
                "RhythmVerseClient",
                Guid.NewGuid().ToString()
            );
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            DestPath = Path.Combine(home, ".clonehero/Songs");
            Directory.CreateDirectory(TempPath);

            foreach (var songPath in songPaths)
            {
                var importPath = Path.Combine(TempPath, Path.GetFileName(songPath));
                Process.Start(new ProcessStartInfo
                {
                    FileName = "./tools/onyx",
                    Arguments = $"import \"{songPath}\" --to \"{importPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = true
                })?.WaitForExit();
            }
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

        public async Task RunAsync()
        {
            await Task.Run(() =>
            {
                foreach (var directory in Directory.GetDirectories(TempPath))
                {
                    if (Directory.GetFiles(directory, "song.yml").Length == 0)
                        continue;

                    var file = Path.Combine(directory, "song.yml");
                    ProcessYaml(file);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "./tools/onyx",
                        Arguments = $"build \"{file}\" --target ps --to \"{DestPath}\"",
                        CreateNoWindow = true,
                        UseShellExecute = true
                    })?.WaitForExit();
                }
            });
        }
    }
}
