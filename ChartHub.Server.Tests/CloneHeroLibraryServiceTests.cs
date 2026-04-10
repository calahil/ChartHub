using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Tests;

public sealed class CloneHeroLibraryServiceTests
{
    [Fact]
    public void SoftDeleteAndRestoreSongUpdateVisibility()
    {
        string root = CreateTempDirectory();
        try
        {
            IOptions<ServerPathOptions> options = Microsoft.Extensions.Options.Options.Create(new ServerPathOptions
            {
                CloneHeroRoot = root,
            });
            CloneHeroLibraryService sut = new(options);

            IReadOnlyList<ChartHub.Server.Contracts.CloneHeroSongResponse> initial = sut.ListSongs();
            Assert.NotEmpty(initial);
            string songId = initial[0].SongId;

            Assert.True(sut.TrySoftDeleteSong(songId, out ChartHub.Server.Contracts.CloneHeroSongResponse? deleted));
            Assert.NotNull(deleted);
            Assert.False(sut.TryGetSong(songId, out _));

            Assert.True(sut.TryRestoreSong(songId, out ChartHub.Server.Contracts.CloneHeroSongResponse? restored));
            Assert.NotNull(restored);
            Assert.True(sut.TryGetSong(songId, out ChartHub.Server.Contracts.CloneHeroSongResponse? found));
            Assert.NotNull(found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InstallFromStagedZipInstallsAndRegistersSong()
    {
        string root = CreateTempDirectory();
        string staging = CreateTempDirectory();
        try
        {
            string stagedZip = Path.Combine(staging, "song.zip");
            string payloadRoot = Path.Combine(staging, "payload");
            Directory.CreateDirectory(payloadRoot);
            File.WriteAllText(Path.Combine(payloadRoot, "notes.chart"), "chart-data");
            System.IO.Compression.ZipFile.CreateFromDirectory(payloadRoot, stagedZip);

            IOptions<ServerPathOptions> options = Microsoft.Extensions.Options.Options.Create(new ServerPathOptions
            {
                CloneHeroRoot = root,
            });
            CloneHeroLibraryService sut = new(options);

            var jobId = Guid.Parse("7f5f9f01-e665-4a6d-bf57-f65cf62a7f12");
            bool installed = sut.TryInstallFromStaged(jobId, "Artist - Song", stagedZip, out ChartHub.Server.Contracts.CloneHeroSongResponse? song, out string? installedPath);

            Assert.True(installed);
            Assert.NotNull(song);
            Assert.NotNull(installedPath);
            Assert.True(Directory.Exists(installedPath));
            Assert.True(File.Exists(Path.Combine(installedPath, "notes.chart")));
            Assert.True(sut.TryGetSong(song!.SongId, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(staging, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "charthub-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
