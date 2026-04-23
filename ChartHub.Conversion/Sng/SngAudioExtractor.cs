namespace ChartHub.Conversion.Sng;

/// <summary>
/// Extracts supported audio payloads from a parsed SNGPKG container.
/// </summary>
internal static class SngAudioExtractor
{
    private static readonly string[] SupportedAudioExtensions = [".opus", ".ogg"];

    internal static async Task<IReadOnlyList<string>> ExtractAsync(
        SngPackage package,
        byte[] containerBytes,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var writtenFiles = new List<string>();

        foreach (SngFileEntry entry in package.Files)
        {
            string extension = Path.GetExtension(entry.Name);
            if (!SupportedAudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string safeFileName = Path.GetFileName(entry.Name);
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                continue;
            }

            byte[] audioBytes = SngPackageReader.ReadFileData(containerBytes, entry);
            string outputPath = Path.Combine(outputDirectory, safeFileName);

            await File.WriteAllBytesAsync(outputPath, audioBytes, cancellationToken).ConfigureAwait(false);
            writtenFiles.Add(outputPath);
        }

        if (writtenFiles.Count == 0)
        {
            throw new InvalidDataException("No supported audio entries (.opus or .ogg) found in SNG package.");
        }

        return writtenFiles;
    }
}