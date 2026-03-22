using System.Reflection;

using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;

namespace ChartHub.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class WatcherFileTypeResolverTests
{
    [Theory]
    [InlineData(WatcherFileType.Rar, "rar.png")]
    [InlineData(WatcherFileType.Zip, "zip.png")]
    [InlineData(WatcherFileType.Con, "rb.png")]
    [InlineData(WatcherFileType.SevenZip, "sevenzip.png")]
    [InlineData(WatcherFileType.CloneHero, "clonehero.png")]
    [InlineData(WatcherFileType.Unknown, "blank.png")]
    public void GetIconForFileType_ReturnsCorrectIconPath(WatcherFileType fileType, string expectedFileName)
    {
        string result = WatcherFileTypeResolver.GetIconForFileType(fileType);

        Assert.EndsWith(expectedFileName, result, StringComparison.Ordinal);
        Assert.StartsWith("avares://ChartHub/", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFileTypeAsync_WithZipExtension_ReturnsZip()
    {
        using var temp = new TemporaryDirectoryFixture("watcher-file-type-zip");
        string path = temp.GetPath("song.zip");
        await File.WriteAllBytesAsync(path, [0x50, 0x4B, 0x03, 0x04]);

        WatcherFileType result = await WatcherFileTypeResolver.GetFileTypeAsync(path);

        Assert.Equal(WatcherFileType.Zip, result);
    }

    [Fact]
    public async Task GetFileTypeAsync_WithRarExtension_ReturnsRar()
    {
        using var temp = new TemporaryDirectoryFixture("watcher-file-type-rar");
        string path = temp.GetPath("song.rar");
        await File.WriteAllBytesAsync(path, new byte[] { 0x52, 0x61, 0x72, 0x21, 0x00, 0x00 });

        WatcherFileType result = await WatcherFileTypeResolver.GetFileTypeAsync(path);

        Assert.Equal(WatcherFileType.Rar, result);
    }

    [Fact]
    public async Task GetFileTypeAsync_WithSevenZipExtension_ReturnsSevenZip()
    {
        using var temp = new TemporaryDirectoryFixture("watcher-file-type-7z");
        string path = temp.GetPath("song.7z");
        await File.WriteAllBytesAsync(path, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]);

        WatcherFileType result = await WatcherFileTypeResolver.GetFileTypeAsync(path);

        Assert.Equal(WatcherFileType.SevenZip, result);
    }

    [Fact]
    public async Task GetFileTypeAsync_WithDirectoryPath_ReturnsCloneHero()
    {
        using var temp = new TemporaryDirectoryFixture("watcher-file-type-dir");
        string dirPath = temp.GetPath("song-folder");
        Directory.CreateDirectory(dirPath);

        WatcherFileType result = await WatcherFileTypeResolver.GetFileTypeAsync(dirPath);

        Assert.Equal(WatcherFileType.CloneHero, result);
    }

    [Fact]
    public async Task GetFileTypeAsync_WithUnknownContent_ReturnsUnknown()
    {
        using var temp = new TemporaryDirectoryFixture("watcher-file-type-unknown");
        string path = temp.GetPath("song.bin");
        await File.WriteAllBytesAsync(path, [0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

        WatcherFileType result = await WatcherFileTypeResolver.GetFileTypeAsync(path);

        Assert.Equal(WatcherFileType.Unknown, result);
    }
}
