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

        var artistSegment = SanitizeSchemaSegment(metadata.Artist, "Unknown Artist");
        var songSegment = SanitizeSchemaSegment(metadata.Title, "Unknown Song");
        var charterSegment = SanitizeSchemaSegment(metadata.Charter, "Unknown Charter");
        var sourceSegment = NormalizeSource(source);
        var leafSegment = $"{charterSegment}__{sourceSegment}";

        var artistPath = Path.Combine(cloneHeroSongsRoot, artistSegment);
        var songPath = Path.Combine(artistPath, songSegment);
        var finalPath = Path.Combine(songPath, leafSegment);

        if (!exists(finalPath))
        {
            return new CloneHeroDirectoryLayout(
                ArtistSegment: artistSegment,
                SongSegment: songSegment,
                CharterSourceSegment: leafSegment,
                RelativePath: Path.Combine(artistSegment, songSegment, leafSegment),
                FullPath: finalPath);
        }

        var counter = 2;
        while (true)
        {
            var candidateLeaf = $"{leafSegment}_{counter}";
            var candidate = Path.Combine(songPath, candidateLeaf);
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
        var flattened = value?
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');

        return SafePathHelper.SanitizeFileName(flattened, fallback);
    }
}
