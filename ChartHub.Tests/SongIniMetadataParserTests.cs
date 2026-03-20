using ChartHub.Services;

namespace ChartHub.Tests;

public class SongIniMetadataParserTests
{
    [Theory]
    [InlineData("song-sample1.ini", "Godspeed You! Black Emperor", "East Hastings (28 Days Later version)", "XEntombmentX")]
    [InlineData("song-sample2.ini", "Lynyrd Skynyrd", "Tuesday's Gone", "Harmonix")]
    [InlineData("song-sample3.ini", "Smashing Pumpkins", "Tonight, Tonight", "BearzUnlimited")]
    [InlineData("song-sample4.ini", "Tool", "Sober", "Convour/clintilona/nunchuck/DenVaktare")]
    public void ParseFromSongIni_ReadsExpectedFields(string fileName, string expectedArtist, string expectedTitle, string expectedCharter)
    {
        var parser = new SongIniMetadataParser();
        var path = Path.Combine(AppContext.BaseDirectory, "Samples", fileName);

        var metadata = parser.ParseFromSongIni(path);

        Assert.Equal(expectedArtist, metadata.Artist);
        Assert.Equal(expectedTitle, metadata.Title);
        Assert.Equal(expectedCharter, metadata.Charter);
    }

    [Fact]
    public void ParseFromSongIni_WhenMissingFile_ReturnsUnknownFallbacks()
    {
        var parser = new SongIniMetadataParser();

        var metadata = parser.ParseFromSongIni(Path.Combine(AppContext.BaseDirectory, "Samples", "missing.ini"));

        Assert.Equal("Unknown Artist", metadata.Artist);
        Assert.Equal("Unknown Song", metadata.Title);
        Assert.Equal("Unknown Charter", metadata.Charter);
    }
}
