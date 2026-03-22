namespace ChartHub.Utilities;

public static class SafePathHelper
{
    public static string SanitizePathSegment(string? segment, string fallback = "item")
    {
        string candidate = string.IsNullOrWhiteSpace(segment)
            ? fallback
            : segment;

        candidate = candidate
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_')
            .Trim();

        foreach (char ch in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(ch, '_');
        }

        if (string.IsNullOrWhiteSpace(candidate) || candidate is "." or "..")
        {
            return fallback;
        }

        return candidate;
    }

    public static string SanitizeFileName(string? fileName, string fallback = "file")
    {
        string candidate = string.IsNullOrWhiteSpace(fileName)
            ? fallback
            : Path.GetFileName(fileName.Replace('\\', '/'));

        foreach (char ch in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(ch, '_');
        }

        candidate = candidate.Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_')
            .Trim();

        if (string.IsNullOrWhiteSpace(candidate) || candidate is "." or "..")
        {
            return fallback;
        }

        return candidate;
    }

    public static string GetSafeFilePath(string rootDirectory, string? fileName, string fallback = "file")
    {
        string rootFullPath = GetNormalizedRoot(rootDirectory);
        string safeFileName = SanitizeFileName(fileName, fallback);
        string destinationPath = Path.GetFullPath(Path.Combine(rootFullPath, safeFileName));
        EnsureWithinRoot(rootFullPath, destinationPath);
        return destinationPath;
    }

    public static string GetSafeArchiveExtractionPath(string rootDirectory, string? archiveEntryPath, string fallback = "file")
    {
        string rootFullPath = GetNormalizedRoot(rootDirectory);
        string rawPath = archiveEntryPath?.Replace('\\', '/') ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new InvalidDataException("Archive entry path is empty.");
        }

        if (rawPath.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(rawPath))
        {
            throw new InvalidDataException($"Archive entry '{archiveEntryPath}' uses a rooted path.");
        }

        string[] segments = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new InvalidDataException("Archive entry path is empty.");
        }

        var safeSegments = new List<string>(segments.Length);
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i].Trim();
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                throw new InvalidDataException($"Archive entry '{archiveEntryPath}' attempts directory traversal.");
            }

            string safeSegment = SanitizeFileName(segment, i == segments.Length - 1 ? fallback : "entry");
            safeSegments.Add(safeSegment);
        }

        if (safeSegments.Count == 0)
        {
            throw new InvalidDataException("Archive entry path did not contain any usable segments.");
        }

        string destinationPath = Path.GetFullPath(Path.Combine(rootFullPath, Path.Combine(safeSegments.ToArray())));
        EnsureWithinRoot(rootFullPath, destinationPath);
        return destinationPath;
    }

    private static string GetNormalizedRoot(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Root directory is required.", nameof(rootDirectory));
        }

        string rootFullPath = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(rootFullPath);
        return rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void EnsureWithinRoot(string rootFullPath, string candidateFullPath)
    {
        if (candidateFullPath.Equals(rootFullPath, StringComparison.Ordinal))
        {
            return;
        }

        string prefix = rootFullPath + Path.DirectorySeparatorChar;
        if (!candidateFullPath.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Path '{candidateFullPath}' escapes root '{rootFullPath}'.");
        }
    }
}