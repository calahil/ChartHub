namespace ChartHub.Conversion.Tests.Parity;

/// <summary>
/// Proves that same-song pairs with different chart formats or container types
/// each produce a canonically correct normalized output.
/// Validates M5 same-song cross-format and cross-container equivalence requirements.
/// </summary>
public sealed class OracleParityEquivalenceTests
{
    [Fact]
    public async Task SameSongPair_WhyGo_NotesMidSourceProducesNotesMidCanonical()
    {
        if (!ParityPaths.IsOracleEnabled())
        {
            return;
        }

        string repoRoot = ParityPaths.GetRepositoryRoot();
        ParityFixtureManifest fixtureManifest = ParityManifestIO.LoadFixtureManifest(
            ParityPaths.GetFixtureManifestPath(repoRoot),
            repoRoot);

        ParityFixtureDefinition fixture = fixtureManifest.Fixtures.Single(item =>
            string.Equals(item.Id, "sng-why-go-harmonix", StringComparison.Ordinal));

        string inputPath = Path.Combine(repoRoot, fixture.InputPath.Replace('/', Path.DirectorySeparatorChar));
        string outputPath = ParityPaths.GetChartHubFixtureOutputPath(
            ParityPaths.GetArtifactsRoot(repoRoot), fixture.Id);

        string finalOutput = await ChartHubParityRunner.EnsureChartHubOutputAsync(inputPath, outputPath);

        IReadOnlyList<ParityChecksumFile> files = ParityManifestIO.BuildChecksumsForDirectory(finalOutput);

        Assert.True(
            files.Any(file => string.Equals(Path.GetFileName(file.Path), "notes.mid", StringComparison.OrdinalIgnoreCase)),
            "notes.mid source (Harmonix) must produce notes.mid in canonical output.");

        Assert.False(
            files.Any(file => string.Equals(Path.GetFileName(file.Path), "notes.chart", StringComparison.OrdinalIgnoreCase)),
            "notes.mid source (Harmonix) must not retain notes.chart in canonical output after normalization.");
    }

    [Fact]
    public async Task SameSongPair_WhyGo_NotesChartSourceProducesNotesChartCanonical()
    {
        if (!ParityPaths.IsOracleEnabled())
        {
            return;
        }

        string repoRoot = ParityPaths.GetRepositoryRoot();
        ParityFixtureManifest fixtureManifest = ParityManifestIO.LoadFixtureManifest(
            ParityPaths.GetFixtureManifestPath(repoRoot),
            repoRoot);

        ParityFixtureDefinition fixture = fixtureManifest.Fixtures.Single(item =>
            string.Equals(item.Id, "sng-why-go-highfine", StringComparison.Ordinal));

        string inputPath = Path.Combine(repoRoot, fixture.InputPath.Replace('/', Path.DirectorySeparatorChar));
        string outputPath = ParityPaths.GetChartHubFixtureOutputPath(
            ParityPaths.GetArtifactsRoot(repoRoot), fixture.Id);

        string finalOutput = await ChartHubParityRunner.EnsureChartHubOutputAsync(inputPath, outputPath);

        IReadOnlyList<ParityChecksumFile> files = ParityManifestIO.BuildChecksumsForDirectory(finalOutput);

        Assert.True(
            files.Any(file => string.Equals(Path.GetFileName(file.Path), "notes.chart", StringComparison.OrdinalIgnoreCase)),
            "notes.chart source (highfine) must produce notes.chart in canonical output.");

        Assert.False(
            files.Any(file => string.Equals(Path.GetFileName(file.Path), "notes.mid", StringComparison.OrdinalIgnoreCase)),
            "notes.chart source (highfine) must not produce notes.mid when source contains only notes.chart.");
    }

    [Fact]
    public async Task SameSongPair_Snuff_Rb3ConAndSngBothProduceNotesMidCanonical()
    {
        if (!ParityPaths.IsOracleEnabled())
        {
            return;
        }

        string repoRoot = ParityPaths.GetRepositoryRoot();
        ParityFixtureManifest fixtureManifest = ParityManifestIO.LoadFixtureManifest(
            ParityPaths.GetFixtureManifestPath(repoRoot),
            repoRoot);

        string artifactsRoot = ParityPaths.GetArtifactsRoot(repoRoot);

        ParityFixtureDefinition rb3conFixture = fixtureManifest.Fixtures.Single(item =>
            string.Equals(item.Id, "rb3con-snuff", StringComparison.Ordinal));
        string rb3conInput = Path.Combine(repoRoot, rb3conFixture.InputPath.Replace('/', Path.DirectorySeparatorChar));
        string rb3conOutput = ParityPaths.GetChartHubFixtureOutputPath(artifactsRoot, rb3conFixture.Id);
        string rb3conFinal = await ChartHubParityRunner.EnsureChartHubOutputAsync(rb3conInput, rb3conOutput);

        ParityFixtureDefinition sngFixture = fixtureManifest.Fixtures.Single(item =>
            string.Equals(item.Id, "sng-snuff-harmonix", StringComparison.Ordinal));
        string sngInput = Path.Combine(repoRoot, sngFixture.InputPath.Replace('/', Path.DirectorySeparatorChar));
        string sngOutput = ParityPaths.GetChartHubFixtureOutputPath(artifactsRoot, sngFixture.Id);
        string sngFinal = await ChartHubParityRunner.EnsureChartHubOutputAsync(sngInput, sngOutput);

        IReadOnlyList<ParityChecksumFile> rb3conFiles = ParityManifestIO.BuildChecksumsForDirectory(rb3conFinal);
        IReadOnlyList<ParityChecksumFile> sngFiles = ParityManifestIO.BuildChecksumsForDirectory(sngFinal);

        Assert.True(
            rb3conFiles.Any(file => string.Equals(Path.GetFileName(file.Path), "notes.mid", StringComparison.OrdinalIgnoreCase)),
            "Snuff RB3CON conversion must produce notes.mid in canonical output.");

        Assert.True(
            sngFiles.Any(file => string.Equals(Path.GetFileName(file.Path), "notes.mid", StringComparison.OrdinalIgnoreCase)),
            "Snuff SNG conversion must produce notes.mid in canonical output.");

        var rb3conRoles = rb3conFiles
            .Where(file => string.Equals(file.Comparison, "functional", StringComparison.Ordinal))
            .Select(file => file.Role)
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToList();

        var sngRoles = sngFiles
            .Where(file => string.Equals(file.Comparison, "functional", StringComparison.Ordinal))
            .Select(file => file.Role)
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(rb3conRoles, sngRoles);
    }
}
