using ChartHub.Server.Contracts;
using ChartHub.Server.Services;

namespace ChartHub.Server.Endpoints;

public static class CloneHeroEndpoints
{
    public static RouteGroupBuilder MapCloneHeroEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/clonehero")
            .WithTags("CloneHero")
            .RequireAuthorization();

        group.MapGet("/songs", (ICloneHeroLibraryService service) => Results.Ok(service.ListSongs()))
            .WithName("ListCloneHeroSongs")
            .WithSummary("List Clone Hero songs");

        group.MapGet("/songs/{songId}", (string songId, ICloneHeroLibraryService service) =>
            service.TryGetSong(songId, out CloneHeroSongResponse? song)
                ? Results.Ok(song)
                : Results.NotFound())
            .WithName("GetCloneHeroSong")
            .WithSummary("Get Clone Hero song details");

        group.MapDelete("/songs/{songId}", (string songId, ICloneHeroLibraryService service) =>
            service.TrySoftDeleteSong(songId, out CloneHeroSongResponse? song)
                ? Results.Ok(song)
                : Results.NotFound())
            .WithName("SoftDeleteCloneHeroSong")
            .WithSummary("Soft-delete a Clone Hero song");

        group.MapPost("/songs/{songId}/restore", (string songId, ICloneHeroLibraryService service) =>
            service.TryRestoreSong(songId, out CloneHeroSongResponse? song)
                ? Results.Ok(song)
                : Results.NotFound())
            .WithName("RestoreCloneHeroSong")
            .WithSummary("Restore a soft-deleted Clone Hero song");

        group.MapPatch("/songs/{songId}/metadata", (
            string songId,
            SongIniPatchRequest request,
            ICloneHeroLibraryService library,
            ISongIniPatchService patchService) =>
        {
            if (!library.TryGetSong(songId, out CloneHeroSongResponse? song) || song is null)
            {
                return Results.NotFound(new { error = "Song not found." });
            }

            if (song.InstalledPath is null)
            {
                return Results.BadRequest(new { error = "Song is not installed; metadata cannot be edited." });
            }

            string songIniPath = Path.Combine(song.InstalledPath, "song.ini");

            SongIniPatchFields fields = new(
                Artist: request.Artist,
                Title: request.Title,
                Charter: request.Charter,
                Genre: request.Genre,
                Year: request.Year,
                DifficultyBand: request.DifficultyBand);

            patchService.PatchSongIni(songIniPath, fields);

            // Persist DB changes for the fields tracked in the library.
            bool anyLibraryField = request.Artist is not null || request.Title is not null || request.Charter is not null;
            if (anyLibraryField)
            {
                library.UpsertInstalledSong(new CloneHeroLibraryUpsertRequest(
                    Source: song.Source,
                    SourceId: song.SourceId,
                    Artist: request.Artist ?? song.Artist,
                    Title: request.Title ?? song.Title,
                    Charter: request.Charter ?? song.Charter,
                    SourceMd5: song.SourceMd5,
                    SourceChartHash: song.SourceChartHash,
                    SourceUrl: song.SourceUrl,
                    InstalledPath: song.InstalledPath,
                    InstalledRelativePath: song.InstalledRelativePath ?? string.Empty));
            }

            library.TryGetSong(songId, out CloneHeroSongResponse? updated);
            return Results.Ok(updated);
        })
        .WithName("PatchCloneHeroSongMetadata")
        .WithSummary("Patch song.ini metadata fields for an installed song.");

        return group;
    }
}
