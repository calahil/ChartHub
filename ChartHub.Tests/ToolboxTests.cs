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

    [Fact]
    public void DebugResetSongProcessor_MovesPhaseShiftFilesAndClearsMusicDirectories()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"charthub-toolbox-tests-{Guid.NewGuid():N}");
        string phaseshiftDir = Path.Combine(tempRoot, "phaseshift");
        string downloadDir = Path.Combine(tempRoot, "downloads");
        string phaseshiftMusicDir = Path.Combine(tempRoot, "music");
        string musicSubDir = Path.Combine(phaseshiftMusicDir, "song-a");

        Directory.CreateDirectory(phaseshiftDir);
        Directory.CreateDirectory(downloadDir);
        Directory.CreateDirectory(musicSubDir);

        string sourceChart = Path.Combine(phaseshiftDir, "notes.chart");
        string sourceIni = Path.Combine(phaseshiftDir, "song.ini");
        string musicFile = Path.Combine(musicSubDir, "audio.ogg");

        File.WriteAllText(sourceChart, "chart-data");
        File.WriteAllText(sourceIni, "ini-data");
        File.WriteAllText(musicFile, "audio-data");

        try
        {
            Toolbox.DebugResetSongProcessor(phaseshiftDir, downloadDir, phaseshiftMusicDir);

            Assert.False(File.Exists(sourceChart));
            Assert.False(File.Exists(sourceIni));
            Assert.True(File.Exists(Path.Combine(downloadDir, "notes.chart")));
            Assert.True(File.Exists(Path.Combine(downloadDir, "song.ini")));

            Assert.False(Directory.Exists(musicSubDir));
            Assert.Empty(Directory.GetDirectories(phaseshiftMusicDir));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
