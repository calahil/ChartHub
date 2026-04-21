using ChartHub.Server.Services;

namespace ChartHub.Server.Tests;

public sealed class DeviceNameNormalizerTests
{
    [Fact]
    public void NormalizeEmptyOrWhitespaceReturnsFallback()
    {
        Assert.Equal("unknown-device", DeviceNameNormalizer.Normalize(null));
        Assert.Equal("unknown-device", DeviceNameNormalizer.Normalize("   \t \r\n"));
    }

    [Fact]
    public void NormalizeCollapsesWhitespaceAndTrims()
    {
        string value = DeviceNameNormalizer.Normalize("  Pixel\t\t  8   Pro  ");

        Assert.Equal("Pixel 8 Pro", value);
    }

    [Fact]
    public void NormalizeRemovesControlCharacters()
    {
        string value = DeviceNameNormalizer.Normalize("Pixel\u0000\u0007 8\n");

        Assert.Equal("Pixel 8", value);
    }

    [Fact]
    public void NormalizeTruncatesToMaxLength()
    {
        string raw = new('A', 90);

        string value = DeviceNameNormalizer.Normalize(raw);

        Assert.Equal(64, value.Length);
        Assert.Equal(new string('A', 64), value);
    }
}