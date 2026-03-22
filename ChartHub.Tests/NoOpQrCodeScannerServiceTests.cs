using ChartHub.Services;

namespace ChartHub.Tests;

[Trait(TestInfrastructure.TestCategories.Category, TestInfrastructure.TestCategories.Unit)]
public class NoOpQrCodeScannerServiceTests
{
    [Fact]
    public void IsSupported_ReturnsFalse()
    {
        var sut = new NoOpQrCodeScannerService();

        Assert.False(sut.IsSupported);
    }

    [Fact]
    public async Task ScanAsync_ReturnsNull()
    {
        var sut = new NoOpQrCodeScannerService();

        string? result = await sut.ScanAsync();

        Assert.Null(result);
    }
}
