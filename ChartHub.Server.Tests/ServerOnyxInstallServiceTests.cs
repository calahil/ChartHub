using ChartHub.Server.Services;

namespace ChartHub.Server.Tests;

public sealed class ServerOnyxInstallServiceTests
{
    [Fact]
    public void ResolveOnyxExecutablePathWhenSecondRootContainsToolReturnsExpectedPath()
    {
        using var temp = new TempDirectory();
        string firstRoot = Path.Combine(temp.Path, "root-a");
        string secondRoot = Path.Combine(temp.Path, "root-b");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(Path.Combine(secondRoot, "tools"));

        string expectedPath = Path.GetFullPath(Path.Combine(secondRoot, "tools", "onyx"));
        File.WriteAllText(expectedPath, "tool");

        string resolved = ServerOnyxInstallService.ResolveOnyxExecutablePath(
            [firstRoot, secondRoot],
            File.Exists);

        Assert.Equal(expectedPath, resolved);
    }

    [Fact]
    public void ResolveOnyxExecutablePathWhenNoTrustedRootContainsToolThrows()
    {
        using var temp = new TempDirectory();
        string firstRoot = Path.Combine(temp.Path, "root-a");
        string secondRoot = Path.Combine(temp.Path, "root-b");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);

        FileNotFoundException ex = Assert.Throws<FileNotFoundException>(() =>
            ServerOnyxInstallService.ResolveOnyxExecutablePath([firstRoot, secondRoot], File.Exists));

        Assert.Equal("Unable to locate the Onyx executable in a trusted tools directory.", ex.Message);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "charthub-server-onyx-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
