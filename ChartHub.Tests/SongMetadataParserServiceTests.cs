using System;
using System.Threading.Tasks;

using ChartHub.Services;

using Xunit;

namespace ChartHub.Tests;

public class SongMetadataParserServiceTests
{
    private readonly SongMetadataParserService _parser = new();

    [Fact]
    public void Parse_ValidIniWithAllFields_ReturnsCorrectMetadata()
    {
        string content = """
[song]
name = Custom Song Title
artist = Custom Artist
charter = Custom Charter
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Custom Song Title", metadata.Title);
        Assert.Equal("Custom Artist", metadata.Artist);
        Assert.Equal("Custom Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_CaseInsensitiveSectionName_ReturnsCorrectMetadata()
    {
        string content = """
[SONG]
name = Title
artist = Artist
charter = Charter
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Title", metadata.Title);
        Assert.Equal("Artist", metadata.Artist);
        Assert.Equal("Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_CaseInsensitiveKeys_ReturnsCorrectMetadata()
    {
        string content = """
[song]
NAME = Title123
ARTIST = Artist123
CHARTER = Charter123
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Title123", metadata.Title);
        Assert.Equal("Artist123", metadata.Artist);
        Assert.Equal("Charter123", metadata.Charter);
    }

    [Fact]
    public void Parse_MixedCaseKeys_ReturnsCorrectMetadata()
    {
        string content = """
[Song]
Name = MixedTitle
ArtisT = MixedArtist
ChArTer = MixedCharter
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("MixedTitle", metadata.Title);
        Assert.Equal("MixedArtist", metadata.Artist);
        Assert.Equal("MixedCharter", metadata.Charter);
    }

    [Fact]
    public void Parse_MissingAllFields_UsesFallbacks()
    {
        string content = """
[song]
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Unknown Song", metadata.Title);
        Assert.Equal("Unknown Artist", metadata.Artist);
        Assert.Equal("Unknown Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_MissingArtistOnly_UsesFallbackArtist()
    {
        string content = """
[song]
name = Title
charter = Charter
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Title", metadata.Title);
        Assert.Equal("Unknown Artist", metadata.Artist);
        Assert.Equal("Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_MissingTitleOnly_UsesFallbackTitle()
    {
        string content = """
[song]
artist = Artist
charter = Charter
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Unknown Song", metadata.Title);
        Assert.Equal("Artist", metadata.Artist);
        Assert.Equal("Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_MissingCharterOnly_UsesFallbackCharter()
    {
        string content = """
[song]
name = Title
artist = Artist
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Title", metadata.Title);
        Assert.Equal("Artist", metadata.Artist);
        Assert.Equal("Unknown Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_IgnoresCommentLines_WithDoubleSlash()
    {
        string content = """
[song]
// This is a comment
name = Title
// Another comment
artist = Artist
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Title", metadata.Title);
        Assert.Equal("Artist", metadata.Artist);
    }

    [Fact]
    public void Parse_IgnoresCommentLines_WithSemicolon()
    {
        string content = """
[song]
; This is a comment
name = Title
; Another comment
artist = Artist
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Title", metadata.Title);
        Assert.Equal("Artist", metadata.Artist);
    }

    [Fact]
    public void Parse_SurvivesMalformedLines_WithoutEquals()
    {
        string content = """
[song]
name = Title
malformed line without equals
artist = Artist
""";

        SongMetadata metadata = _parser.ParseContent(content);

        // Should parse name and artist despite malformed line
        Assert.Equal("Title", metadata.Title);
        Assert.Equal("Artist", metadata.Artist);
    }

    [Fact]
    public void Parse_SurvivesMalformedLines_WithOnlyEquals()
    {
        string content = """
[song]
name = Title
=
artist = Artist
""";

        SongMetadata metadata = _parser.ParseContent(content);

        // Should parse name and artist despite malformed line
        Assert.Equal("Title", metadata.Title);
        Assert.Equal("Artist", metadata.Artist);
    }

    [Fact]
    public void Parse_SurvivesMalformedLines_WithMultipleEquals()
    {
        string content = """
[song]
name = Title
artist = A = B
charter = Charter
""";

        SongMetadata metadata = _parser.ParseContent(content);

        // Should parse correctly; value is everything after first =
        Assert.Equal("Title", metadata.Title);
        Assert.Equal("A = B", metadata.Artist);
        Assert.Equal("Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_IgnoresEmptyLines()
    {
        string content = """
[song]

name = Title

artist = Artist

charter = Charter

""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Title", metadata.Title);
        Assert.Equal("Artist", metadata.Artist);
        Assert.Equal("Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_TrimsWhitespace_AroundKeys()
    {
        string content = """
[song]
  name  = Title
  artist  = Artist
  charter  = Charter
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Title", metadata.Title);
        Assert.Equal("Artist", metadata.Artist);
        Assert.Equal("Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_TrimsWhitespace_AroundValues()
    {
        string content = """
[song]
name =   Title   
artist =   Artist   
charter =   Charter   
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Title", metadata.Title);
        Assert.Equal("Artist", metadata.Artist);
        Assert.Equal("Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_IgnoresKeysOutsideSongSection_Previous()
    {
        string content = """
[metadata]
name = WrongTitle
artist = WrongArtist
[song]
name = CorrectTitle
artist = CorrectArtist
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("CorrectTitle", metadata.Title);
        Assert.Equal("CorrectArtist", metadata.Artist);
    }

    [Fact]
    public void Parse_IgnoresKeysOutsideSongSection_After()
    {
        string content = """
[song]
name = CorrectTitle
artist = CorrectArtist
[other]
name = WrongTitle
artist = WrongArtist
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("CorrectTitle", metadata.Title);
        Assert.Equal("CorrectArtist", metadata.Artist);
    }

    [Fact]
    public void Parse_IgnoresUnknownKeys_InSongSection()
    {
        string content = """
[song]
name = Title
unknown_key = Unknown Value
artist = Artist
another_unknown = Another Value
charter = Charter
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Title", metadata.Title);
        Assert.Equal("Artist", metadata.Artist);
        Assert.Equal("Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_EmptyContent_UsesFallbacks()
    {
        string content = "";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Unknown Song", metadata.Title);
        Assert.Equal("Unknown Artist", metadata.Artist);
        Assert.Equal("Unknown Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_EmptyValues_UsesFallbacks()
    {
        string content = """
[song]
name = 
artist = 
charter = 
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Unknown Song", metadata.Title);
        Assert.Equal("Unknown Artist", metadata.Artist);
        Assert.Equal("Unknown Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_WhitespaceOnlyValues_UsesFallbacks()
    {
        string content = """
[song]
name =    
artist =    
charter =    
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Unknown Song", metadata.Title);
        Assert.Equal("Unknown Artist", metadata.Artist);
        Assert.Equal("Unknown Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_NoSongSection_UsesFallbacks()
    {
        string content = """
[metadata]
name = Title
artist = Artist
charter = Charter
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Unknown Song", metadata.Title);
        Assert.Equal("Unknown Artist", metadata.Artist);
        Assert.Equal("Unknown Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_SpecialCharactersInValues_PreservesCorrectly()
    {
        string content = """
[song]
name = Song (Remix) [Expert]
artist = Band & Friends feat. Guest
charter = Charte@r_19$!
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Song (Remix) [Expert]", metadata.Title);
        Assert.Equal("Band & Friends feat. Guest", metadata.Artist);
        Assert.Equal("Charte@r_19$!", metadata.Charter);
    }

    [Fact]
    public void Parse_UnicodeCharacters_PreservesCorrectly()
    {
        string content = """
[song]
name = Москиткова Песня
artist = 中文アーティスト
charter = Чартер
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Москиткова Песня", metadata.Title);
        Assert.Equal("中文アーティスト", metadata.Artist);
        Assert.Equal("Чартер", metadata.Charter);
    }

    [Fact]
    public void Parse_LinesArray_WorksCorrectly()
    {
        string[] lines = new[]
        {
            "[song]",
            "name = Title",
            "artist = Artist",
            "charter = Charter"
        };

        SongMetadata metadata = _parser.Parse(lines);

        Assert.Equal("Title", metadata.Title);
        Assert.Equal("Artist", metadata.Artist);
        Assert.Equal("Charter", metadata.Charter);
    }

    [Fact]
    public async Task ParseAsync_FileExists_ReadsAndParsesCorrectly()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"test_song_{Guid.NewGuid()}.ini");
        try
        {
            string content = """
[song]
name = Title
artist = Artist
charter = Charter
""";
            await File.WriteAllTextAsync(tempFile, content);

            SongMetadata metadata = await _parser.ParseAsync(tempFile);

            Assert.Equal("Title", metadata.Title);
            Assert.Equal("Artist", metadata.Artist);
            Assert.Equal("Charter", metadata.Charter);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task ParseAsync_FileMissing_UsesFallbacks()
    {
        string nonExistentFile = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.ini");

        SongMetadata metadata = await _parser.ParseAsync(nonExistentFile);

        Assert.Equal("Unknown Song", metadata.Title);
        Assert.Equal("Unknown Artist", metadata.Artist);
        Assert.Equal("Unknown Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_DuplicateKeys_LastValueWins()
    {
        string content = """
[song]
name = First Title
name = Second Title
artist = First Artist
artist = Second Artist
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Second Title", metadata.Title);
        Assert.Equal("Second Artist", metadata.Artist);
    }

    [Fact]
    public void Parse_RealWorldExample_DrumFill()
    {
        string content = """
[song]
name = Drum Fills
artist = DrumFill Artist
charter = DrumFill Charter
offset = 0
resolution = 480
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Drum Fills", metadata.Title);
        Assert.Equal("DrumFill Artist", metadata.Artist);
        Assert.Equal("DrumFill Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_RealWorldExample_WithComments()
    {
        string content = """
; Clone Hero Song File
[song]
; Song metadata
name = Song Name
artist = Song Artist
; Charter info
charter = Song Charter
; Ignore this
resolution = 480
offset = 0
""";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Song Name", metadata.Title);
        Assert.Equal("Song Artist", metadata.Artist);
        Assert.Equal("Song Charter", metadata.Charter);
    }

    [Fact]
    public void Parse_AllFallbacksWhenNoFieldsSet()
    {
        string content = "[song]";

        SongMetadata metadata = _parser.ParseContent(content);

        Assert.Equal("Unknown Artist", metadata.Artist);
        Assert.Equal("Unknown Song", metadata.Title);
        Assert.Equal("Unknown Charter", metadata.Charter);
    }
}
