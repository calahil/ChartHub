namespace ChartHub.Conversion.Tests.Parity;

public sealed class OracleParityComparisonTests
{
    private static readonly IReadOnlyDictionary<string, string> KnownUnsupportedFixtures =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sng-biology"] = "Onyx oracle cannot extract this encrypted/unsupported SNG fixture.",
            ["rb3con-neighborhood-1"] = "ChartHub does not yet support MOGG encryption version 0x0D.",
            ["sng-creature-comfort"] = "SNG notes.chart import is not implemented yet; ChartHub currently supports notes.mid.",
        };

    [Theory]
    [InlineData("rb3con-ready-to-start")]
    [InlineData("rb3con-neighborhood-1")]
    [InlineData("rb3con-arcade-fire-pack")]
    [InlineData("rb3con-everything-now")]
    [InlineData("sng-biology")]
    [InlineData("sng-release")]
    [InlineData("sng-yellow-ledbetter")]
    [InlineData("sng-creature-comfort")]
    public async Task Fixture_OptInComparison_CanGenerateOutputsAndValidateChecksums(string fixtureId)
    {
        if (!ParityPaths.IsOracleEnabled())
        {
            return;
        }

        string repoRoot = ParityPaths.GetRepositoryRoot();
        ParityFixtureManifest fixtureManifest = ParityManifestIO.LoadFixtureManifest(
            ParityPaths.GetFixtureManifestPath(repoRoot),
            repoRoot);
        ParityChecksumManifest checksumManifest = ParityManifestIO.LoadChecksumManifest(
            ParityPaths.GetChecksumManifestPath(repoRoot));

        ParityFixtureDefinition fixture = fixtureManifest.Fixtures.Single(item => item.Id == fixtureId);

        // Explicitly skip fixtures that are known unsupported by current toolchain.
        // Keep these fixtures in manifests so parity policy remains visible and trackable.
        if (KnownUnsupportedFixtures.ContainsKey(fixtureId))
        {
            return;
        }

        string inputPath = Path.Combine(repoRoot, fixture.InputPath.Replace('/', Path.DirectorySeparatorChar));
        string artifactsRoot = ParityPaths.GetArtifactsRoot(repoRoot);
        string onyxOutput = ParityPaths.GetOracleFixtureOutputPath(artifactsRoot, fixture.Id);

        await OnyxOracleRunner.EnsureOracleOutputAsync(inputPath, onyxOutput);
        Assert.True(Directory.Exists(onyxOutput), $"Missing oracle output directory: {onyxOutput}");

        IReadOnlyList<ParityChecksumFile> onyxFiles = ParityManifestIO.BuildChecksumsForDirectory(onyxOutput);
        Assert.NotEmpty(onyxFiles);

        if (ParityPaths.IsChecksumUpdateEnabled())
        {
            ParityChecksumManifest updatedManifest = ParityManifestIO.UpsertFixture(checksumManifest, fixture.Id, onyxFiles);
            ParityManifestIO.SaveChecksumManifest(ParityPaths.GetChecksumManifestPath(repoRoot), updatedManifest);
            return;
        }

        string chartHubOutput = ParityPaths.GetChartHubFixtureOutputPath(artifactsRoot, fixture.Id);
        string chartHubFinalOutput = await ChartHubParityRunner
            .EnsureChartHubOutputAsync(inputPath, chartHubOutput);

        Assert.True(Directory.Exists(chartHubFinalOutput), $"Missing ChartHub output directory: {chartHubFinalOutput}");
        IReadOnlyList<ParityChecksumFile> chartHubFiles = ParityManifestIO.BuildChecksumsForDirectory(chartHubFinalOutput);
        Assert.NotEmpty(chartHubFiles);

        ParityChecksumFixture? expectedFixture = checksumManifest.Fixtures.SingleOrDefault(item => item.Id == fixture.Id);
        if (expectedFixture is null)
        {
            return;
        }

        var chartHubLookup = chartHubFiles.ToDictionary(item => item.Path, StringComparer.Ordinal);
        foreach (ParityChecksumFile expectedFile in expectedFixture.Files)
        {
            if (string.Equals(expectedFile.Comparison, "byte", StringComparison.Ordinal))
            {
                Assert.True(chartHubLookup.TryGetValue(expectedFile.Path, out ParityChecksumFile? actualFile),
                    $"Missing expected byte-parity file '{expectedFile.Path}' in ChartHub output for fixture '{fixture.Id}'.");

                Assert.Equal(expectedFile.Sha256, actualFile.Sha256);
            }
        }

        var expectedFunctionalByRole = expectedFixture.Files
            .Where(item => string.Equals(item.Comparison, "functional", StringComparison.Ordinal))
            .GroupBy(item => item.Role, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var actualFunctionalByRole = chartHubFiles
            .Where(item => string.Equals(item.Comparison, "functional", StringComparison.Ordinal))
            .GroupBy(item => item.Role, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach ((string role, int expectedCount) in expectedFunctionalByRole)
        {
            Assert.True(actualFunctionalByRole.TryGetValue(role, out int actualCount),
                $"Missing expected functional role '{role}' for fixture '{fixture.Id}'.");
            Assert.True(actualCount >= 1,
                $"Insufficient functional files for role '{role}' in fixture '{fixture.Id}'. Expected at least one file, got {actualCount}.");
        }
    }
}
