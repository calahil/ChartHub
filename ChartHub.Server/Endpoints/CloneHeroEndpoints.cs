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
            service.TryGetSong(songId, out ChartHub.Server.Contracts.CloneHeroSongResponse? song)
                ? Results.Ok(song)
                : Results.NotFound())
            .WithName("GetCloneHeroSong")
            .WithSummary("Get Clone Hero song details");

        group.MapDelete("/songs/{songId}", (string songId, ICloneHeroLibraryService service) =>
            service.TrySoftDeleteSong(songId, out ChartHub.Server.Contracts.CloneHeroSongResponse? song)
                ? Results.Ok(song)
                : Results.NotFound())
            .WithName("SoftDeleteCloneHeroSong")
            .WithSummary("Soft-delete a Clone Hero song");

        group.MapPost("/songs/{songId}/restore", (string songId, ICloneHeroLibraryService service) =>
            service.TryRestoreSong(songId, out ChartHub.Server.Contracts.CloneHeroSongResponse? song)
                ? Results.Ok(song)
                : Results.NotFound())
            .WithName("RestoreCloneHeroSong")
            .WithSummary("Restore a soft-deleted Clone Hero song");

        return group;
    }
}
