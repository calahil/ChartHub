namespace ChartHub.Server.Services;

public static class ServerContentPathResolver
{
    public static string Resolve(string path, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));
        }

        if (string.IsNullOrWhiteSpace(contentRootPath))
        {
            throw new ArgumentException("Content root path cannot be null or whitespace.", nameof(contentRootPath));
        }

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(contentRootPath, path));
    }
}