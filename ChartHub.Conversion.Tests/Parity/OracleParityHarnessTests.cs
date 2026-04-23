namespace ChartHub.Conversion.Tests.Parity;

public sealed class OracleParityHarnessTests
{
    [Fact]
    public void ChecksumManifest_OraclePin_MatchesFixtureManifestPin()
    {
        string repoRoot = ParityPaths.GetRepositoryRoot();
        string fixtureManifestPath = ParityPaths.GetFixtureManifestPath(repoRoot);
        string checksumManifestPath = ParityPaths.GetChecksumManifestPath(repoRoot);

        Assert.True(File.Exists(fixtureManifestPath), $"Missing fixture manifest: {fixtureManifestPath}");
        Assert.True(File.Exists(checksumManifestPath), $"Missing checksum manifest: {checksumManifestPath}");

        ParityFixtureManifest fixtureManifest = ParityManifestIO.LoadFixtureManifest(fixtureManifestPath, repoRoot);
        ParityChecksumManifest checksumManifest = ParityManifestIO.LoadChecksumManifest(checksumManifestPath);

        Assert.True(
            string.Equals(fixtureManifest.Oracle.ReleaseTag, checksumManifest.Oracle.ReleaseTag, StringComparison.Ordinal),
            $"Parity manifest oracle pin is stale. " +
            $"fixtures.yaml pins oracle release '{fixtureManifest.Oracle.ReleaseTag}' " +
            $"but checksums/manifest.yaml was generated from release '{checksumManifest.Oracle.ReleaseTag}'. " +
            $"Regenerate baselines with CH_PARITY_UPDATE_CHECKSUMS=1 after updating the oracle pin.");

        Assert.True(
            string.Equals(fixtureManifest.Oracle.Commit, checksumManifest.Oracle.Commit, StringComparison.Ordinal),
            $"Parity manifest oracle commit is stale. " +
            $"fixtures.yaml pins commit '{fixtureManifest.Oracle.Commit}' " +
            $"but checksums/manifest.yaml was generated from commit '{checksumManifest.Oracle.Commit}'. " +
            $"Regenerate baselines with CH_PARITY_UPDATE_CHECKSUMS=1 after updating the oracle pin.");
    }

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
