using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
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
                SqliteDbPath = Path.Combine(root, "charthub-server.db"),
            });
            CloneHeroLibraryService sut = new(
                options,
                new TestHostEnvironment(root),
                new ServerCloneHeroDirectorySchemaService());

            sut.UpsertInstalledSong(new CloneHeroLibraryUpsertRequest(
                Source: "rhythmverse",
                SourceId: "song-1",
                Artist: "Artist",
                Title: "Title",
                Charter: "Charter",
                SourceMd5: null,
                SourceChartHash: null,
                SourceUrl: "https://example.test/song.zip",
                InstalledPath: Path.Combine(root, "Songs", "Artist", "Title", "Charter__rhythmverse"),
                InstalledRelativePath: Path.Combine("Artist", "Title", "Charter__rhythmverse")));

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

    private sealed class TestHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ChartHub.Server.Tests";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = contentRootPath;

        public string EnvironmentName { get; set; } = Environments.Development;

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
