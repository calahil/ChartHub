using ChartHub.Utilities;

namespace ChartHub.Tests;

[Trait(TestInfrastructure.TestCategories.Category, TestInfrastructure.TestCategories.Unit)]
public class ToolboxTests
{
    [Fact]
    public void ConvertFilter_WithNonEmptyString_RemovesSongPrefixAndLowercases()
    {
        string result = Toolbox.ConvertFilter("Song Title");

        Assert.Equal("title", result);
    }

    [Fact]
    public void ConvertFilter_WithEmptyString_ReturnsEmpty()
    {
        string result = Toolbox.ConvertFilter(string.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ConvertSecondsToText_WithValidValue_ReturnsFormattedTime()
    {
        string result = Toolbox.ConvertSecondstoText(125);

        Assert.Equal("2:05", result);
    }

    [Fact]
    public void ConvertSecondsToText_WithNull_ReturnsZero()
    {
        string result = Toolbox.ConvertSecondstoText(null);

        Assert.Equal("00:00", result);
    }

    [Fact]
    public void ConvertSecondsToText_WithZero_ReturnsZero()
    {
        string result = Toolbox.ConvertSecondstoText(0);

        Assert.Equal("0:00", result);
    }

    [Fact]
    public void ConvertMillisecondsToText_WithValidValue_ReturnsFormattedTime()
    {
        string result = Toolbox.ConvertMillisecondsToText(65000);

        Assert.Equal("1:05", result);
    }

    [Fact]
    public void ConvertMillisecondsToText_WithNull_ReturnsZero()
    {
        string result = Toolbox.ConvertMillisecondsToText(null);

        Assert.Equal("00:00", result);
    }

    [Theory]
    [InlineData("Artist", "Ascending", "ASC")]
    [InlineData("Artist", "Descending", "DESC")]
    [InlineData("Title", "Ascending", "ASC")]
    [InlineData("Title", "Descending", "DESC")]
    public void GetSortOrder_ForStringField_ReturnsStraightOrder(string filter, string order, string expected)
    {
        string result = Toolbox.GetSortOrder(filter, order);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Duration", "Ascending", "DESC")]
    [InlineData("Duration", "Descending", "ASC")]
    public void GetSortOrder_ForNumericalField_ReturnsInvertedOrder(string filter, string order, string expected)
    {
        string result = Toolbox.GetSortOrder(filter, order);

        Assert.Equal(expected, result);
    }
}
