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
        if (string.IsNullOrWhiteSpace(source))
            return LibrarySourceNames.Import;

        var normalized = source.Trim().ToLowerInvariant();
        return normalized switch
        {
            LibrarySourceNames.RhythmVerse => LibrarySourceNames.RhythmVerse,
            LibrarySourceNames.Encore => LibrarySourceNames.Encore,
            LibrarySourceNames.Import => LibrarySourceNames.Import,
            _ => LibrarySourceNames.Import,
        };
    }

    public CloneHeroDirectoryLayout ResolveUniqueLayout(
        string cloneHeroSongsRoot,
        SongMetadata metadata,
        string? source,
        Func<string, bool>? exists = null)
    {
        exists ??= Directory.Exists;

        var artistSegment = SafePathHelper.SanitizePathSegment(metadata.Artist, "Unknown Artist");
        var songSegment = SafePathHelper.SanitizePathSegment(metadata.Title, "Unknown Song");
        var charterSegment = SafePathHelper.SanitizePathSegment(metadata.Charter, "Unknown Charter");
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
}
