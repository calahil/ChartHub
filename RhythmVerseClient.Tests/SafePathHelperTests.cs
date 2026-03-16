using RhythmVerseClient.Utilities;

namespace RhythmVerseClient.Tests;

[Trait(RhythmVerseClient.Tests.TestInfrastructure.TestCategories.Category, RhythmVerseClient.Tests.TestInfrastructure.TestCategories.Unit)]
public class SafePathHelperTests
{
    [Fact]
    public void SanitizeFileName_RemovesTraversalSegmentsAndInvalidCharacters()
    {
        var result = SafePathHelper.SanitizeFileName("../evil\\name?.zip", "fallback.bin");

        Assert.DoesNotContain("..", result);
        Assert.False(result.Contains('/'));
        Assert.False(result.Contains('\\'));
        Assert.EndsWith(".zip", result, StringComparison.Ordinal);
        Assert.Contains("name", result, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSafeFilePath_ProducesPathInsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var safePath = SafePathHelper.GetSafeFilePath(root, "../../track.zip", "fallback.zip");

        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var safeFull = Path.GetFullPath(safePath);
        Assert.StartsWith(rootFull + Path.DirectorySeparatorChar, safeFull, StringComparison.Ordinal);
        Assert.Equal("track.zip", Path.GetFileName(safePath));
    }

    [Fact]
    public void GetSafeArchiveExtractionPath_RejectsTraversalEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidDataException>(() =>
            SafePathHelper.GetSafeArchiveExtractionPath(root, "../outside/song.ini", "fallback.bin"));
    }

    [Fact]
    public void GetSafeArchiveExtractionPath_RejectsRootedEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidDataException>(() =>
            SafePathHelper.GetSafeArchiveExtractionPath(root, "/etc/passwd", "fallback.bin"));
    }

    [Fact]
    public void GetSafeArchiveExtractionPath_AllowsNestedSafeEntriesWithinRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var safePath = SafePathHelper.GetSafeArchiveExtractionPath(root, "Artist/Album/song.chart", "fallback.bin");

        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var safeFull = Path.GetFullPath(safePath);
        Assert.StartsWith(rootFull + Path.DirectorySeparatorChar, safeFull, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("Artist", "Album", "song.chart"), safeFull, StringComparison.Ordinal);
    }
}
