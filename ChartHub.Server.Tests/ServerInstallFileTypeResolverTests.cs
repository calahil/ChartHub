using System.Text;

using ChartHub.Server.Services;

namespace ChartHub.Server.Tests;

public sealed class ServerInstallFileTypeResolverTests
{
    [Fact]
    public async Task ResolveAsyncBinWithZipSignatureReturnsZip()
    {
        using TemporaryFile temp = new(".bin");
        await File.WriteAllBytesAsync(temp.Path, [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00]);

        ServerInstallFileTypeResolver sut = new();
        ServerInstallFileType result = await sut.ResolveAsync(temp.Path);

        Assert.Equal(ServerInstallFileType.Zip, result);
    }

    [Fact]
    public async Task ResolveAsyncBinWithConSignatureReturnsCon()
    {
        using TemporaryFile temp = new(".bin");
        await File.WriteAllBytesAsync(temp.Path, Encoding.UTF8.GetBytes("CON "));

        ServerInstallFileTypeResolver sut = new();
        ServerInstallFileType result = await sut.ResolveAsync(temp.Path);

        Assert.Equal(ServerInstallFileType.Con, result);
    }

    [Fact]
    public async Task ResolveAsyncBinWithHtmlPayloadReturnsUnknown()
    {
        using TemporaryFile temp = new(".bin");
        await File.WriteAllTextAsync(temp.Path, "<!doctype html><html><body>Google Drive</body></html>");

        ServerInstallFileTypeResolver sut = new();
        ServerInstallFileType result = await sut.ResolveAsync(temp.Path);

        Assert.Equal(ServerInstallFileType.Unknown, result);
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(string extension)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"charthub-server-test-{Guid.NewGuid():N}{extension}");
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
