namespace ChartHub.Conversion.Sng;

/// <summary>
/// Extracts album art from a parsed SNGPKG container.
/// </summary>
internal static class SngAlbumArtExtractor
{
    private static readonly string[] PreferredAlbumArtNames = ["album.jpg", "album.jpeg", "album.png"];
    private static readonly string[] SupportedImageExtensions = [".jpg", ".jpeg", ".png"];

    internal static async Task<string> ExtractAsync(
        SngPackage package,
        byte[] containerBytes,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        SngFileEntry? selectedEntry = FindPreferredEntry(package) ?? FindFirstSupportedImageEntry(package);
        if (selectedEntry == null)
        {
            throw new InvalidDataException("No supported album art entries (.jpg, .jpeg, or .png) found in SNG package.");
        }

        byte[] imageBytes = SngPackageReader.ReadFileData(containerBytes, selectedEntry);
        string extension = Path.GetExtension(selectedEntry.Name);
        string outputPath = Path.Combine(outputDirectory, $"album{extension.ToLowerInvariant()}");

        await File.WriteAllBytesAsync(outputPath, imageBytes, cancellationToken).ConfigureAwait(false);
        return outputPath;
    }

    private static SngFileEntry? FindPreferredEntry(SngPackage package)
    {
        foreach (string fileName in PreferredAlbumArtNames)
        {
            if (SngPackageReader.TryFindEntry(package, fileName, out SngFileEntry? entry) && entry != null)
            {
                return entry;
            }
        }

        return null;
    }

    private static SngFileEntry? FindFirstSupportedImageEntry(SngPackage package)
    {
        foreach (SngFileEntry entry in package.Files)
        {
            string extension = Path.GetExtension(entry.Name);
            if (SupportedImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }
}