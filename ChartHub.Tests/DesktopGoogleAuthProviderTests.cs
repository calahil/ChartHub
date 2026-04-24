using ChartHub.Services;

namespace ChartHub.Tests;

public sealed class DesktopGoogleAuthProviderTests
{
    [Fact]
    public void FirstNonWhiteSpace_SkipsNullAndWhitespaceValues()
    {
        string? result = DesktopGoogleAuthProvider.FirstNonWhiteSpace(null, "", "   ", "desktop-client-id");

        Assert.Equal("desktop-client-id", result);
    }

    [Fact]
    public void FirstNonWhiteSpace_ReturnsNullWhenAllValuesAreBlank()
    {
        string? result = DesktopGoogleAuthProvider.FirstNonWhiteSpace(null, "", "   ");

        Assert.Null(result);
    }
}