using ChartHub.Utilities;

namespace ChartHub.Services;

public sealed record CloneHeroDirectoryLayout(
    string ArtistSegment,
    string SongSegment,
    string CharterSourceSegment,
    string RelativePath,
    string FullPath);

public interface ICloneHeroDirectorySchemaService
{
    string NormalizeSource(string? source);

    CloneHeroDirectoryLayout ResolveUniqueLayout(
        string cloneHeroSongsRoot,
        SongMetadata metadata,
        string? source,
        Func<string, bool>? exists = null);
}

public sealed class CloneHeroDirectorySchemaService : ICloneHeroDirectorySchemaService
{
    public string NormalizeSource(string? source)
    {
        return LibrarySourceNames.NormalizeTrustedSource(source);
    }

    public CloneHeroDirectoryLayout ResolveUniqueLayout(
        string cloneHeroSongsRoot,
        SongMetadata metadata,
        string? source,
        Func<string, bool>? exists = null)
    {
        exists ??= Directory.Exists;

        string artistSegment = SanitizeSchemaSegment(metadata.Artist, "Unknown Artist");
        string songSegment = SanitizeSchemaSegment(metadata.Title, "Unknown Song");
        string charterSegment = SanitizeSchemaSegment(metadata.Charter, "Unknown Charter");
        string sourceSegment = NormalizeSource(source);
        string leafSegment = $"{charterSegment}__{sourceSegment}";

        string artistPath = Path.Combine(cloneHeroSongsRoot, artistSegment);
        string songPath = Path.Combine(artistPath, songSegment);
        string finalPath = Path.Combine(songPath, leafSegment);

        if (!exists(finalPath))
        {
            return new CloneHeroDirectoryLayout(
                ArtistSegment: artistSegment,
                SongSegment: songSegment,
                CharterSourceSegment: leafSegment,
                RelativePath: Path.Combine(artistSegment, songSegment, leafSegment),
                FullPath: finalPath);
        }

        int counter = 2;
        while (true)
        {
            string candidateLeaf = $"{leafSegment}_{counter}";
            string candidate = Path.Combine(songPath, candidateLeaf);
            if (!exists(candidate))
            {
                return new CloneHeroDirectoryLayout(
                    ArtistSegment: artistSegment,
                    SongSegment: songSegment,
                    CharterSourceSegment: candidateLeaf,
                    RelativePath: Path.Combine(artistSegment, songSegment, candidateLeaf),
                    FullPath: candidate);
            }

            counter++;
        }
    }

    private static string SanitizeSchemaSegment(string? value, string fallback)
    {
        // Preserve slash-separated tokens by flattening separators before file-name sanitization.
        string? flattened = value?
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');

        return SafePathHelper.SanitizeFileName(flattened, fallback);
    }
}
