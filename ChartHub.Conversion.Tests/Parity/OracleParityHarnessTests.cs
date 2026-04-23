namespace ChartHub.Conversion.Tests.Parity;

public sealed class OracleParityHarnessTests
{
    [Fact]
    public void OracleHarness_OptInEnvironment_HasRequiredFilesAndPaths()
    {
        if (!ParityPaths.IsOracleEnabled())
        {
            return;
        }

        string repoRoot = ParityPaths.GetRepositoryRoot();
        string fixtureManifestPath = ParityPaths.GetFixtureManifestPath(repoRoot);
        string checksumManifestPath = ParityPaths.GetChecksumManifestPath(repoRoot);

        Assert.True(File.Exists(fixtureManifestPath), $"Missing fixture manifest: {fixtureManifestPath}");
        Assert.True(File.Exists(checksumManifestPath), $"Missing checksum manifest: {checksumManifestPath}");

        string? onyxBinary = Environment.GetEnvironmentVariable(ParityPaths.OracleBinaryEnv);
        Assert.False(string.IsNullOrWhiteSpace(onyxBinary),
            $"{ParityPaths.OracleBinaryEnv} must be set when {ParityPaths.OracleEnableEnv}=1.");
        Assert.True(File.Exists(onyxBinary), $"Onyx binary not found: {onyxBinary}");

        string? onyxArgsTemplate = Environment.GetEnvironmentVariable(ParityPaths.OracleArgsTemplateEnv);
        Assert.False(string.IsNullOrWhiteSpace(onyxArgsTemplate),
            $"{ParityPaths.OracleArgsTemplateEnv} must be set. Use placeholders {{input}} and {{output}}.");

        string artifactsRoot = ParityPaths.GetArtifactsRoot(repoRoot);
        Assert.False(Path.IsPathRooted(artifactsRoot) == false && artifactsRoot.StartsWith("..", StringComparison.Ordinal),
            "Artifact root must not traverse outside the repository.");

        ParityFixtureManifest fixtureManifest = ParityManifestIO.LoadFixtureManifest(fixtureManifestPath, repoRoot);
        ParityChecksumManifest checksumManifest = ParityManifestIO.LoadChecksumManifest(checksumManifestPath);

        Assert.NotEmpty(fixtureManifest.Fixtures);
        Assert.Equal(fixtureManifest.Oracle.ReleaseTag, checksumManifest.Oracle.ReleaseTag);
        Assert.Equal(fixtureManifest.Oracle.Commit, checksumManifest.Oracle.Commit);
    }
}
