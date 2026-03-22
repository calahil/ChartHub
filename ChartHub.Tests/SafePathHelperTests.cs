using ChartHub.Utilities;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class SafePathHelperTests
{
    [Fact]
    public void SanitizeFileName_RemovesTraversalSegmentsAndInvalidCharacters()
    {
        string result = SafePathHelper.SanitizeFileName("../evil\\name?.zip", "fallback.bin");

        Assert.DoesNotContain("..", result);
        Assert.False(result.Contains('/'));
        Assert.False(result.Contains('\\'));
        Assert.EndsWith(".zip", result, StringComparison.Ordinal);
        Assert.Contains("name", result, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSafeFilePath_ProducesPathInsideRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        string safePath = SafePathHelper.GetSafeFilePath(root, "../../track.zip", "fallback.zip");

        string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string safeFull = Path.GetFullPath(safePath);
        Assert.StartsWith(rootFull + Path.DirectorySeparatorChar, safeFull, StringComparison.Ordinal);
        Assert.Equal("track.zip", Path.GetFileName(safePath));
    }

    [Fact]
    public void GetSafeArchiveExtractionPath_RejectsTraversalEntries()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidDataException>(() =>
            SafePathHelper.GetSafeArchiveExtractionPath(root, "../outside/song.ini", "fallback.bin"));
    }

    [Fact]
    public void GetSafeArchiveExtractionPath_RejectsRootedEntries()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidDataException>(() =>
            SafePathHelper.GetSafeArchiveExtractionPath(root, "/etc/passwd", "fallback.bin"));
    }

    [Fact]
    public void GetSafeArchiveExtractionPath_AllowsNestedSafeEntriesWithinRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        string safePath = SafePathHelper.GetSafeArchiveExtractionPath(root, "Artist/Album/song.chart", "fallback.bin");

        string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string safeFull = Path.GetFullPath(safePath);
        Assert.StartsWith(rootFull + Path.DirectorySeparatorChar, safeFull, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine("Artist", "Album", "song.chart"), safeFull, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizePathSegment_WithValidSegment_ReturnsSegment()
    {
        string result = SafePathHelper.SanitizePathSegment("valid-segment");

        Assert.Equal("valid-segment", result);
    }

    [Fact]
    public void SanitizePathSegment_WithNullInput_ReturnsFallback()
    {
        string result = SafePathHelper.SanitizePathSegment(null, "fallback");

        Assert.Equal("fallback", result);
    }

    [Fact]
    public void SanitizePathSegment_WithWhitespaceInput_ReturnsFallback()
    {
        string result = SafePathHelper.SanitizePathSegment("   ", "fallback");

        Assert.Equal("fallback", result);
    }

    [Fact]
    public void SanitizePathSegment_WithDotDot_ReturnsFallback()
    {
        string result = SafePathHelper.SanitizePathSegment("..", "fallback");

        Assert.Equal("fallback", result);
    }

    [Fact]
    public void SanitizePathSegment_WithSingleDot_ReturnsFallback()
    {
        string result = SafePathHelper.SanitizePathSegment(".", "fallback");

        Assert.Equal("fallback", result);
    }

    [Fact]
    public void SanitizePathSegment_WithDirectorySeparator_ReplacesSeparator()
    {
        string result = SafePathHelper.SanitizePathSegment("seg/ment");

        Assert.DoesNotContain("/", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeFileName_WithNullInput_ReturnsFallback()
    {
        string result = SafePathHelper.SanitizeFileName(null, "fallback.bin");

        Assert.Equal("fallback.bin", result);
    }
}
