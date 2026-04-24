using System.Security.Cryptography;
using System.Text;

using YamlDotNet.RepresentationModel;

namespace ChartHub.Conversion.Tests.Parity;

internal sealed record OracleBaseline(
    string Tool,
    string ReleaseTag,
    string Commit,
    string TransformedMediaParity,
    string NonTranscodedParity);

internal sealed record ParityFixtureDefinition(
    string Id,
    string InputPath,
    string InputType,
    string TransformedMediaParity,
    string NonTranscodedParity);

internal sealed record ParityFixtureManifest(
    int Version,
    OracleBaseline Oracle,
    IReadOnlyList<ParityFixtureDefinition> Fixtures);

internal sealed record ParityChecksumFile(
    string Path,
    string Sha256,
    string Role,
    string Comparison);

internal sealed record ParityChecksumFixture(
    string Id,
    IReadOnlyList<ParityChecksumFile> Files);

internal sealed record ParityChecksumManifest(
    int Version,
    OracleBaseline Oracle,
    IReadOnlyList<ParityChecksumFixture> Fixtures);

internal static class ParityManifestIO
{
    private static readonly HashSet<string> TransformedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mogg",
        ".ogg",
        ".opus",
        ".mp3",
        ".wav",
        ".mid",
        ".midi",
        ".chart",
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".ini",
    };

    // Paths produced by the Onyx oracle that are tool-internal and have no ChartHub equivalent.
    // Exclude these when building checksums so they are never written to the manifest.
    private static readonly string[] OracleInternalPrefixes =
    [
        "onyx-repack/",
        "onyx-project/",
    ];

    // File extensions produced by the Onyx oracle that ChartHub never outputs.
    // .milo_xbox etc. — Rock Band 3 animation/choreography data, not relevant to Clone Hero.
    // .png_xbox etc. — Xbox-specific art format, ChartHub converts to standard PNG.
    private static readonly string[] OracleInternalSuffixes =
    [
        ".milo_xbox",
        ".milo_ps3",
        ".milo_wii",
        ".png_xbox",
        ".jpg_xbox",
        ".dta",
        "_RS2.xml",
        ".mp4",
    ];

    internal static ParityFixtureManifest LoadFixtureManifest(string path, string repoRoot)
    {
        using var reader = new StreamReader(path);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidOperationException("Fixture manifest must contain a root mapping.");
        }

        ValidateTopLevelKeys(root, ["version", "oracle", "fixtures"], "fixture manifest");

        int version = int.Parse(GetRequiredScalar(root, "version"), System.Globalization.CultureInfo.InvariantCulture);
        OracleBaseline oracle = ParseOracle(root, requireParityPolicy: true);
        YamlSequenceNode fixturesNode = GetRequiredSequence(root, "fixtures");
        HashSet<string> ids = new(StringComparer.Ordinal);
        List<ParityFixtureDefinition> fixtures = [];

        foreach (YamlNode node in fixturesNode.Children)
        {
            if (node is not YamlMappingNode fixtureNode)
            {
                throw new InvalidOperationException("Each fixture entry must be a mapping.");
            }

            ValidateTopLevelKeys(fixtureNode, ["id", "inputPath", "inputType", "expected"], "fixture entry");

            string id = GetRequiredScalar(fixtureNode, "id");
            if (!ids.Add(id))
            {
                throw new InvalidOperationException($"Duplicate fixture id '{id}' in fixture manifest.");
            }

            string inputPath = GetRequiredScalar(fixtureNode, "inputPath");
            string inputType = GetRequiredScalar(fixtureNode, "inputType");
            YamlMappingNode expectedNode = GetRequiredMapping(fixtureNode, "expected");
            ValidateTopLevelKeys(expectedNode, ["transformedMediaParity", "nonTranscodedParity"], $"fixture '{id}' expected section");

            string fullInputPath = Path.Combine(repoRoot, inputPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullInputPath))
            {
                throw new InvalidOperationException($"Fixture '{id}' missing input file: {inputPath}");
            }

            fixtures.Add(new ParityFixtureDefinition(
                Id: id,
                InputPath: inputPath,
                InputType: inputType,
                TransformedMediaParity: GetRequiredScalar(expectedNode, "transformedMediaParity"),
                NonTranscodedParity: GetRequiredScalar(expectedNode, "nonTranscodedParity")));
        }

        if (fixtures.Count == 0)
        {
            throw new InvalidOperationException("Fixture manifest must contain at least one fixture.");
        }

        return new ParityFixtureManifest(version, oracle, fixtures);
    }

    internal static ParityChecksumManifest LoadChecksumManifest(string path)
    {
        using var reader = new StreamReader(path);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidOperationException("Checksum manifest must contain a root mapping.");
        }

        ValidateTopLevelKeys(root, ["version", "oracle", "fixtures"], "checksum manifest");
        int version = int.Parse(GetRequiredScalar(root, "version"), System.Globalization.CultureInfo.InvariantCulture);
        OracleBaseline oracle = ParseOracle(root, requireParityPolicy: false);
        YamlSequenceNode fixturesNode = GetRequiredSequence(root, "fixtures");
        List<ParityChecksumFixture> fixtures = [];
        HashSet<string> ids = new(StringComparer.Ordinal);

        foreach (YamlNode node in fixturesNode.Children)
        {
            if (node is not YamlMappingNode fixtureNode)
            {
                throw new InvalidOperationException("Each checksum fixture entry must be a mapping.");
            }

            ValidateTopLevelKeys(fixtureNode, ["id", "files"], "checksum fixture entry");
            string id = GetRequiredScalar(fixtureNode, "id");
            if (!ids.Add(id))
            {
                throw new InvalidOperationException($"Duplicate checksum fixture id '{id}'.");
            }

            List<ParityChecksumFile> files = [];
            YamlSequenceNode filesNode = GetRequiredSequence(fixtureNode, "files");
            foreach (YamlNode fileNode in filesNode.Children)
            {
                if (fileNode is not YamlMappingNode fileMapping)
                {
                    throw new InvalidOperationException($"Checksum file entry for fixture '{id}' must be a mapping.");
                }

                ValidateTopLevelKeys(fileMapping, ["path", "sha256", "role", "comparison"], $"checksum file entry for '{id}'");
                files.Add(new ParityChecksumFile(
                    Path: GetRequiredScalar(fileMapping, "path"),
                    Sha256: GetRequiredScalar(fileMapping, "sha256"),
                    Role: GetRequiredScalar(fileMapping, "role"),
                    Comparison: GetRequiredScalar(fileMapping, "comparison")));
            }

            fixtures.Add(new ParityChecksumFixture(id, files));
        }

        return new ParityChecksumManifest(version, oracle, fixtures);
    }

    internal static void SaveChecksumManifest(string path, ParityChecksumManifest manifest)
    {
        StringBuilder builder = new();
        builder.AppendLine($"version: {manifest.Version}");
        builder.AppendLine("oracle:");
        builder.AppendLine($"  tool: {Quote(manifest.Oracle.Tool)}");
        builder.AppendLine($"  releaseTag: {Quote(manifest.Oracle.ReleaseTag)}");
        builder.AppendLine($"  commit: {Quote(manifest.Oracle.Commit)}");
        builder.AppendLine();
        builder.AppendLine("fixtures:");

        foreach (ParityChecksumFixture fixture in manifest.Fixtures.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            builder.AppendLine($"  - id: {Quote(fixture.Id)}");
            builder.AppendLine("    files:");
            foreach (ParityChecksumFile file in fixture.Files.OrderBy(item => item.Path, StringComparer.Ordinal))
            {
                builder.AppendLine($"      - path: {Quote(file.Path)}");
                builder.AppendLine($"        sha256: {Quote(file.Sha256)}");
                builder.AppendLine($"        role: {Quote(file.Role)}");
                builder.AppendLine($"        comparison: {Quote(file.Comparison)}");
            }
        }

        File.WriteAllText(path, builder.ToString());
    }

    internal static IReadOnlyList<ParityChecksumFile> BuildChecksumsForDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Parity output directory not found: {directoryPath}");
        }

        var files = Directory
            .EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path =>
            {
                string relativePath = Path.GetRelativePath(directoryPath, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                return new ParityChecksumFile(
                    Path: relativePath,
                    Sha256: ComputeSha256(path),
                    Role: DetermineRole(relativePath),
                    Comparison: DetermineComparison(relativePath));
            })
            .Where(file => !OracleInternalPrefixes.Any(prefix =>
                file.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                && !OracleInternalSuffixes.Any(suffix =>
                file.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return NormalizeCanonicalChartFiles(files);
    }

    private static IReadOnlyList<ParityChecksumFile> NormalizeCanonicalChartFiles(
        IReadOnlyList<ParityChecksumFile> files)
    {
        var canonicalDirectories = files
            .Where(file => string.Equals(Path.GetFileName(file.Path), "notes.mid", StringComparison.OrdinalIgnoreCase))
            .Select(file => NormalizeDirectory(Path.GetDirectoryName(file.Path)))
            .ToHashSet(StringComparer.Ordinal);

        return files
            .Where(file =>
            {
                if (!string.Equals(Path.GetFileName(file.Path), "notes.chart", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string directory = NormalizeDirectory(Path.GetDirectoryName(file.Path));
                return !canonicalDirectories.Contains(directory);
            })
            .ToList();
    }

    private static string NormalizeDirectory(string? directory)
    {
        return string.IsNullOrEmpty(directory)
            ? string.Empty
            : directory.Replace(Path.DirectorySeparatorChar, '/');
    }

    internal static ParityChecksumManifest UpsertFixture(
        ParityChecksumManifest manifest,
        string fixtureId,
        IReadOnlyList<ParityChecksumFile> files)
    {
        var fixtures = manifest.Fixtures
            .Where(item => !string.Equals(item.Id, fixtureId, StringComparison.Ordinal))
            .Append(new ParityChecksumFixture(fixtureId, files))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        return new ParityChecksumManifest(manifest.Version, manifest.Oracle, fixtures);
    }

    private static OracleBaseline ParseOracle(YamlMappingNode root, bool requireParityPolicy)
    {
        YamlMappingNode oracleNode = GetRequiredMapping(root, "oracle");
        if (requireParityPolicy)
        {
            ValidateTopLevelKeys(oracleNode, ["tool", "releaseTag", "commit", "parityPolicy"], "oracle section");
            YamlMappingNode parityPolicyNode = GetRequiredMapping(oracleNode, "parityPolicy");
            ValidateTopLevelKeys(parityPolicyNode, ["transformedMedia", "nonTranscodedFiles"], "oracle parityPolicy section");
            return new OracleBaseline(
                Tool: GetRequiredScalar(oracleNode, "tool"),
                ReleaseTag: GetRequiredScalar(oracleNode, "releaseTag"),
                Commit: GetRequiredScalar(oracleNode, "commit"),
                TransformedMediaParity: GetRequiredScalar(parityPolicyNode, "transformedMedia"),
                NonTranscodedParity: GetRequiredScalar(parityPolicyNode, "nonTranscodedFiles"));
        }

        ValidateTopLevelKeys(oracleNode, ["tool", "releaseTag", "commit"], "oracle section");
        return new OracleBaseline(
            Tool: GetRequiredScalar(oracleNode, "tool"),
            ReleaseTag: GetRequiredScalar(oracleNode, "releaseTag"),
            Commit: GetRequiredScalar(oracleNode, "commit"),
            TransformedMediaParity: string.Empty,
            NonTranscodedParity: string.Empty);
    }

    private static string DetermineComparison(string relativePath)
    {
        string extension = Path.GetExtension(relativePath);
        return TransformedExtensions.Contains(extension) ? "functional" : "byte";
    }

    private static string DetermineRole(string relativePath)
    {
        string extension = Path.GetExtension(relativePath);
        return extension.ToLowerInvariant() switch
        {
            ".mogg" or ".ogg" or ".opus" or ".mp3" or ".wav" => "audio",
            ".mid" or ".midi" or ".chart" => "chart",
            ".ini" => "metadata",
            ".png" or ".jpg" or ".jpeg" or ".webp" => "art",
            _ => "artifact",
        };
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private static void ValidateTopLevelKeys(YamlMappingNode node, IReadOnlyCollection<string> allowedKeys, string sectionName)
    {
        foreach (YamlNode keyNode in node.Children.Keys)
        {
            if (keyNode is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
            {
                throw new InvalidOperationException($"{sectionName} contains a non-scalar key.");
            }

            if (!allowedKeys.Contains(scalar.Value, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected key '{scalar.Value}' in {sectionName}.");
            }
        }
    }

    private static YamlMappingNode GetRequiredMapping(YamlMappingNode node, string key)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? valueNode) || valueNode is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException($"Missing required mapping '{key}'.");
        }

        return mapping;
    }

    private static YamlSequenceNode GetRequiredSequence(YamlMappingNode node, string key)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? valueNode) || valueNode is not YamlSequenceNode sequence)
        {
            throw new InvalidOperationException($"Missing required sequence '{key}'.");
        }

        return sequence;
    }

    private static string GetRequiredScalar(YamlMappingNode node, string key)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? valueNode) || valueNode is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
        {
            throw new InvalidOperationException($"Missing required scalar '{key}'.");
        }

        return scalar.Value;
    }
}
