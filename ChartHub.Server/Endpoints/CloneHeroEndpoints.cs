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

        group.MapPost("/install-from-staged/{jobId:guid}", (Guid jobId, IDownloadJobStore store, ICloneHeroLibraryService service) =>
            {
                if (!store.TryGet(jobId, out ChartHub.Server.Contracts.DownloadJobResponse? job) || job is null)
                {
                    return Results.NotFound();
                }

                if (string.IsNullOrWhiteSpace(job.StagedPath))
                {
                    return Results.Conflict(new { error = "Job does not have a staged artifact." });
                }

                if (!service.TryInstallFromStaged(job.JobId, job.DisplayName, job.StagedPath, out ChartHub.Server.Contracts.CloneHeroSongResponse? song, out string? installedPath)
                    || song is null
                    || string.IsNullOrWhiteSpace(installedPath))
                {
                    return Results.BadRequest(new { error = "Unable to install staged artifact into Clone Hero library." });
                }

                store.MarkInstalled(job.JobId, installedPath);
                return Results.Accepted($"/api/v1/clonehero/songs/{song.SongId}", song);
            })
            .WithName("InstallFromStaged")
            .WithSummary("Trigger install from staged artifacts");

        return group;
    }
}
