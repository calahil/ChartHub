using ChartHub.Server.Services;

namespace ChartHub.Server.Tests;

public sealed class ServerContentPathResolverTests
{
    [Fact]
    public void ResolveWhenPathIsRelativeReturnsAbsolutePathUnderContentRoot()
    {
        string contentRoot = Path.Combine(Path.GetTempPath(), "charthub-server-content-root", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);

        string resolved = ServerContentPathResolver.Resolve("dev-data/charthub/staging", contentRoot);

        Assert.Equal(Path.GetFullPath(Path.Combine(contentRoot, "dev-data/charthub/staging")), resolved);
        Directory.Delete(contentRoot, recursive: true);
    }

    [Fact]
    public void ResolveWhenPathIsAbsoluteReturnsNormalizedAbsolutePath()
    {
        string absolutePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "charthub-server-absolute", Guid.NewGuid().ToString("N")));

        string resolved = ServerContentPathResolver.Resolve(absolutePath, Path.GetTempPath());

        Assert.Equal(absolutePath, resolved);
    }
}
