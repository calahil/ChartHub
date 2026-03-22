using ChartHub.Services;

namespace ChartHub.Tests;

[Trait(TestInfrastructure.TestCategories.Category, TestInfrastructure.TestCategories.Unit)]
public class SyncAdvertisedUrlOptionsProviderTests
{
    [Fact]
    public void GetAdvertisedUrlOptions_WithNullListenPrefix_ReturnsOptions()
    {
        var sut = new SyncAdvertisedUrlOptionsProvider();

        IReadOnlyList<string> result = sut.GetAdvertisedUrlOptions(null, null);

        Assert.NotNull(result);
    }

    [Fact]
    public void GetAdvertisedUrlOptions_WithSpecificListenPrefix_UsesPortFromPrefix()
    {
        var sut = new SyncAdvertisedUrlOptionsProvider();

        IReadOnlyList<string> result = sut.GetAdvertisedUrlOptions("http://0.0.0.0:19876/", null);

        Assert.NotNull(result);
        Assert.All(result, url => Assert.Contains("19876", url, StringComparison.Ordinal));
    }

    [Fact]
    public void GetAdvertisedUrlOptions_WithCurrentAdvertisedBaseUrl_IncludesItWhenNotAlreadyPresent()
    {
        var sut = new SyncAdvertisedUrlOptionsProvider();

        IReadOnlyList<string> result = sut.GetAdvertisedUrlOptions(null, "http://192.168.99.200:15123");

        Assert.Contains(result, url => url.Contains("192.168.99.200", StringComparison.Ordinal));
    }

    [Fact]
    public void GetAdvertisedUrlOptions_WithInvalidListenPrefix_FallsBackToDefault()
    {
        var sut = new SyncAdvertisedUrlOptionsProvider();

        IReadOnlyList<string> result = sut.GetAdvertisedUrlOptions("not-a-valid-url-!!!!", null);

        Assert.NotNull(result);
    }

    [Fact]
    public void GetAdvertisedUrlOptions_WithListenPrefixWithoutScheme_NormalizesToHttp()
    {
        var sut = new SyncAdvertisedUrlOptionsProvider();

        IReadOnlyList<string> result = sut.GetAdvertisedUrlOptions("0.0.0.0:15123", null);

        Assert.NotNull(result);
    }

    [Fact]
    public void GetAdvertisedUrlOptions_WithAdvertisedBaseUrlWithoutScheme_NormalizesToHttp()
    {
        var sut = new SyncAdvertisedUrlOptionsProvider();

        IReadOnlyList<string> result = sut.GetAdvertisedUrlOptions(null, "192.168.1.100:15123");

        Assert.Contains(result, url => url.Contains("192.168.1.100", StringComparison.Ordinal));
    }

    [Fact]
    public void GetAdvertisedUrlOptions_WithEmptyAdvertisedBaseUrl_DoesNotAddEmptyEntry()
    {
        var sut = new SyncAdvertisedUrlOptionsProvider();

        IReadOnlyList<string> result = sut.GetAdvertisedUrlOptions(null, "   ");

        Assert.All(result, url => Assert.False(string.IsNullOrWhiteSpace(url)));
    }
}
