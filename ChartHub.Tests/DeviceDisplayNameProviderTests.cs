using System.Reflection;

using ChartHub.Services;

namespace ChartHub.Tests;

[Trait(TestInfrastructure.TestCategories.Category, TestInfrastructure.TestCategories.Unit)]
public class DeviceDisplayNameProviderTests
{
    [Fact]
    public void NormalizeDeviceName_WithNullInput_ReturnsFallback()
    {
        string result = InvokeNormalizeDeviceName(null, "fallback");

        Assert.Equal("fallback", result);
    }

    [Fact]
    public void NormalizeDeviceName_RemovesControlCharsAndCollapsesWhitespace()
    {
        string raw = "  Pixel\t\t\n\r\0   8   Pro  ";

        string result = InvokeNormalizeDeviceName(raw, "fallback");

        Assert.Equal("Pixel 8 Pro", result);
    }

    [Fact]
    public void NormalizeDeviceName_TruncatesTo64Characters()
    {
        string raw = new('x', 80);

        string result = InvokeNormalizeDeviceName(raw, "fallback");

        Assert.Equal(64, result.Length);
        Assert.Equal(new string('x', 64), result);
    }

    [Fact]
    public void GetDisplayName_WithoutOverride_ReturnsNormalizedNonEmptyName()
    {
        var provider = new DeviceDisplayNameProvider();

        string result = provider.GetDisplayName();

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain('\n', result);
        Assert.DoesNotContain('\r', result);
    }

    private static string InvokeNormalizeDeviceName(string? raw, string fallback)
    {
        MethodInfo? method = typeof(DeviceDisplayNameProvider).GetMethod(
            "NormalizeDeviceName",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        object? result = method.Invoke(null, new object?[] { raw, fallback });

        Assert.IsType<string>(result);
        return (string)result;
    }
}