using YamlDotNet.RepresentationModel;

namespace ChartHub.Conversion.Tests.Parity;

public sealed class OracleParityHarnessTests
{
    private const string OracleEnableEnv = "CH_PARITY_ENABLE_ORACLE";
    private const string OracleBinaryEnv = "CH_PARITY_ONYX_BIN";
    private const string OracleArtifactsRootEnv = "CH_PARITY_ARTIFACTS_ROOT";

    [Fact]
    public void OracleHarness_OptInEnvironment_HasRequiredFilesAndPaths()
    {
        if (!IsOracleEnabled())
        {
            return;
        }

        string repoRoot = GetRepositoryRoot();
        string fixtureManifestPath = Path.Combine(repoRoot, "parity", "fixtures.yaml");
        string checksumManifestPath = Path.Combine(repoRoot, "parity", "checksums", "manifest.yaml");

        Assert.True(File.Exists(fixtureManifestPath), $"Missing fixture manifest: {fixtureManifestPath}");
        Assert.True(File.Exists(checksumManifestPath), $"Missing checksum manifest: {checksumManifestPath}");

        string? onyxBinary = Environment.GetEnvironmentVariable(OracleBinaryEnv);
        Assert.False(string.IsNullOrWhiteSpace(onyxBinary),
            $"{OracleBinaryEnv} must be set when {OracleEnableEnv}=1.");
        Assert.True(File.Exists(onyxBinary), $"Onyx binary not found: {onyxBinary}");

        string artifactsRoot = Environment.GetEnvironmentVariable(OracleArtifactsRootEnv)
            ?? Path.Combine(repoRoot, ".parity-artifacts");
        Assert.False(Path.IsPathRooted(artifactsRoot) == false && artifactsRoot.StartsWith("..", StringComparison.Ordinal),
            "Artifact root must not traverse outside the repository.");

        ValidateFixtureManifest(fixtureManifestPath, repoRoot);
    }

    private static bool IsOracleEnabled()
    {
        return string.Equals(Environment.GetEnvironmentVariable(OracleEnableEnv), "1", StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine(current, "ChartHub.sln");
            if (File.Exists(candidate))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate repository root (ChartHub.sln).");
    }

    private static void ValidateFixtureManifest(string fixtureManifestPath, string repoRoot)
    {
        using var reader = new StreamReader(fixtureManifestPath);
        var yaml = new YamlStream();
        yaml.Load(reader);

        Assert.NotEmpty(yaml.Documents);
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;

        Assert.True(root.Children.ContainsKey("fixtures"), "Fixture manifest must contain 'fixtures'.");
        var fixtures = (YamlSequenceNode)root.Children[new YamlScalarNode("fixtures")];
        Assert.NotEmpty(fixtures.Children);

        foreach (YamlNode node in fixtures.Children)
        {
            var fixture = (YamlMappingNode)node;
            string id = GetRequiredScalar(fixture, "id");
            string inputPath = GetRequiredScalar(fixture, "inputPath");

            string fullInputPath = Path.Combine(repoRoot, inputPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(fullInputPath), $"Fixture '{id}' missing input file: {inputPath}");
        }
    }

    private static string GetRequiredScalar(YamlMappingNode node, string key)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? valueNode))
        {
            throw new InvalidOperationException($"Missing required key '{key}' in fixture manifest.");
        }

        if (valueNode is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
        {
            throw new InvalidOperationException($"Key '{key}' must be a non-empty scalar value.");
        }

        return scalar.Value;
    }
}
