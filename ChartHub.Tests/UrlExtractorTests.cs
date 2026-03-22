using ChartHub.Services;

namespace ChartHub.Tests;

[Trait(TestInfrastructure.TestCategories.Category, TestInfrastructure.TestCategories.Unit)]
public class UrlExtractorTests
{
    [Fact]
    public void ExtractIdFromUrl_WithFileUrl_ReturnsFileId()
    {
        string url = "https://drive.google.com/file/d/1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgVE2upms/view";

        string result = UrlExtractor.ExtractIdFromUrl(url);

        Assert.Equal("1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgVE2upms", result);
    }

    [Fact]
    public void ExtractIdFromUrl_WithFolderUrl_ReturnsFolderId()
    {
        string url = "https://drive.google.com/drive/folders/0B7l5uajXXXXXXXXXXXXXXXXX";

        string result = UrlExtractor.ExtractIdFromUrl(url);

        Assert.Equal("0B7l5uajXXXXXXXXXXXXXXXXX", result);
    }

    [Fact]
    public void ExtractIdFromUrl_WithInvalidUrl_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => UrlExtractor.ExtractIdFromUrl("https://example.com/not-a-drive-url"));
    }
}
