namespace ChartHub.Tests.TestInfrastructure;

public sealed class TemporaryDirectoryFixture : IDisposable
{
    public string RootPath { get; }

    public TemporaryDirectoryFixture(string? prefix = null)
    {
        string directoryName = $"{prefix ?? "rv-tests"}-{Guid.NewGuid():N}";
        RootPath = Path.Combine(Path.GetTempPath(), directoryName);
        Directory.CreateDirectory(RootPath);
    }

    public string CreateSubdirectory(string? name = null)
    {
        string directoryName = string.IsNullOrWhiteSpace(name) ? Guid.NewGuid().ToString("N") : name;
        string path = Path.Combine(RootPath, directoryName);
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetPath(string relativePath)
    {
        return Path.Combine(RootPath, relativePath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
