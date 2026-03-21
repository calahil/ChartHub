using ChartHub.Services;

namespace ChartHub.Tests;

public class CloneHeroDirectorySchemaServiceTests
{
    [Fact]
    public void ResolveUniqueLayout_UsesThreeLevelSchemaAndSanitizesCharter()
    {
        var sut = new CloneHeroDirectorySchemaService();
        var root = Path.Combine("/tmp", "songs-root");

        var layout = sut.ResolveUniqueLayout(
            root,
            new SongMetadata("Tool", "Sober", "Convour/clintilona/nunchuck/DenVaktare"),
            "rhythmverse",
            exists: _ => false);

        Assert.Equal(Path.Combine("Tool", "Sober", "Convour_clintilona_nunchuck_DenVaktare__rhythmverse"), layout.RelativePath);
        Assert.EndsWith(Path.Combine("Tool", "Sober", "Convour_clintilona_nunchuck_DenVaktare__rhythmverse"), layout.FullPath, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveUniqueLayout_WhenExists_AppendsSuffix()
    {
        var sut = new CloneHeroDirectorySchemaService();
        var root = Path.Combine("/tmp", "songs-root");

        var existing = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.Combine(root, "Artist", "Song", "Charter__rhythmverse"),
            Path.Combine(root, "Artist", "Song", "Charter__rhythmverse_2"),
        };

        var layout = sut.ResolveUniqueLayout(
            root,
            new SongMetadata("Artist", "Song", "Charter"),
            source: LibrarySourceNames.RhythmVerse,
            exists: existing.Contains);

        Assert.Equal(Path.Combine("Artist", "Song", "Charter__rhythmverse_3"), layout.RelativePath);
    }

    [Fact]
    public void ResolveUniqueLayout_EmptySegments_UseUnknownFallbacks()
    {
        var sut = new CloneHeroDirectorySchemaService();
        var root = Path.Combine("/tmp", "songs-root");

        var layout = sut.ResolveUniqueLayout(
            root,
            new SongMetadata("", "   ", null!),
            "rhythmverse",
            exists: _ => false);

        Assert.Equal(Path.Combine("Unknown Artist", "Unknown Song", "Unknown Charter__rhythmverse"), layout.RelativePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("customsource")]
    public void ResolveUniqueLayout_UnknownSources_Throw(string? source)
    {
        var sut = new CloneHeroDirectorySchemaService();
        var root = Path.Combine("/tmp", "songs-root");

        Assert.Throws<ArgumentException>(() => sut.ResolveUniqueLayout(
            root,
            new SongMetadata("Artist", "Song", "Charter"),
            source,
            exists: _ => false));
    }
}
