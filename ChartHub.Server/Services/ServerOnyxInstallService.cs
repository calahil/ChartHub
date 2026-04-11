using System.Diagnostics;

using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

using YamlDotNet.RepresentationModel;

namespace ChartHub.Server.Services;

public sealed record ServerOnyxInstallResult(string OutputDirectory, ServerSongMetadata Metadata);

public interface IServerOnyxInstallService
{
    Task<ServerOnyxInstallResult> ConvertAsync(string songPath, string sourceSuffix, CancellationToken cancellationToken = default);
}

public sealed class ServerOnyxInstallService : IServerOnyxInstallService
{
    private readonly string _stagingDir;
    private readonly string _contentRootPath;

    public ServerOnyxInstallService(IOptions<ServerPathOptions> pathOptions, IWebHostEnvironment environment)
    {
        ServerPathOptions paths = pathOptions.Value;
        _contentRootPath = environment.ContentRootPath;
        _stagingDir = ServerContentPathResolver.Resolve(paths.StagingDir, _contentRootPath);
    }

    public async Task<ServerOnyxInstallResult> ConvertAsync(string songPath, string sourceSuffix, CancellationToken cancellationToken = default)
    {
        string sourceSongPath = ServerContentPathResolver.Resolve(songPath, _contentRootPath);
        string jobId = Guid.NewGuid().ToString("N");
        string workspaceRoot = Path.Combine(_stagingDir, "onyx", jobId);
        string importRoot = Path.Combine(workspaceRoot, "import");
        string buildRoot = Path.Combine(workspaceRoot, "build");
        string importPath = Path.Combine(importRoot, ServerSafePathHelper.SanitizeFileName(Path.GetFileNameWithoutExtension(sourceSongPath), "import"));

        Directory.CreateDirectory(importRoot);
        Directory.CreateDirectory(buildRoot);

        try
        {
            await RunOnyxAsync(["import", sourceSongPath, "--to", importPath], cancellationToken).ConfigureAwait(false);

            string? songYamlPath = Directory.GetFiles(importPath, "song.yml", SearchOption.AllDirectories).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(songYamlPath))
            {
                throw new InvalidOperationException("Onyx import completed but song.yml was not generated.");
            }

            ServerSongMetadata metadata = PatchSongYaml(songYamlPath, sourceSuffix, sourceSongPath);
            await RunOnyxAsync(["build", songYamlPath, "--target", "ps", "--to", buildRoot], cancellationToken).ConfigureAwait(false);

            string produced = ResolveProducedDirectory(buildRoot);
            return new ServerOnyxInstallResult(produced, metadata);
        }
        catch
        {
            Cleanup(workspaceRoot);
            throw;
        }
    }

    private static ServerSongMetadata PatchSongYaml(string songYamlPath, string sourceSuffix, string fallbackSongPath)
    {
        var yaml = new YamlStream();
        using (var reader = new StreamReader(songYamlPath))
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

        targets.Children[new YamlScalarNode("ps")] = new YamlMappingNode
        {
            { "game", "ps" }
        };

        YamlMappingNode metadata = root.Children.TryGetValue(new YamlScalarNode("metadata"), out YamlNode? metadataNode)
            && metadataNode is YamlMappingNode metadataMapping
            ? metadataMapping
            : new YamlMappingNode();

        string artist = GetYamlScalar(metadata, "artist", "Unknown Artist");
        string title = GetYamlScalar(metadata, "title", Path.GetFileNameWithoutExtension(fallbackSongPath));
        string charter = GetYamlScalar(metadata, "charter", "Unknown Charter");

        using var writer = new StreamWriter(songYamlPath);
        yaml.Save(writer, assignAnchors: false);

        _ = sourceSuffix;
        return new ServerSongMetadata(artist, title, charter);
    }

    private static async Task RunOnyxAsync(string[] arguments, CancellationToken cancellationToken)
    {
        string executablePath = ResolveOnyxExecutablePath();
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

        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            string message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException($"Onyx exited with code {process.ExitCode}: {message}".Trim());
        }
    }

    internal static string ResolveOnyxExecutablePath(IReadOnlyList<string>? roots = null, Func<string, bool>? fileExists = null)
    {
        IReadOnlyList<string> searchRoots = roots ??
        [
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
        ];

        Func<string, bool> exists = fileExists ?? File.Exists;

        foreach (string root in searchRoots)
        {
            string candidate = Path.GetFullPath(Path.Combine(root, "tools", "onyx"));
            if (exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Unable to locate the Onyx executable in a trusted tools directory.");
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

    private static string GetYamlScalar(YamlMappingNode node, string key, string fallback)
    {
        return node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value)
            ? value.ToString() ?? fallback
            : fallback;
    }

    private static void Cleanup(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
