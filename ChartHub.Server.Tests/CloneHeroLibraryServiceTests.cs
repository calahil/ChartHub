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

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "charthub-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
