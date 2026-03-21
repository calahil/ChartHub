using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class LibraryCatalogServiceTests
{
    [Fact]
    public async Task UpsertAsync_StoresAndFindsMembership()
    {
        using var temp = new TemporaryDirectoryFixture("library-catalog-upsert");
        var sut = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        await sut.UpsertAsync(new LibraryCatalogEntry(
            LibrarySourceNames.RhythmVerse,
            "song-123",
            "Song",
            "Artist",
            "Charter",
            "/tmp/song.zip",
            DateTimeOffset.UtcNow));

        var isPresent = await sut.IsInLibraryAsync(LibrarySourceNames.RhythmVerse, "song-123");

        Assert.True(isPresent);
    }

    [Fact]
    public async Task GetMembershipMapAsync_ReturnsOnlyExistingEntries()
    {
        using var temp = new TemporaryDirectoryFixture("library-catalog-membership-map");
        var sut = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        await sut.UpsertAsync(new LibraryCatalogEntry(
            LibrarySourceNames.Encore,
            "md5-one",
            "Song One",
            "Artist",
            null,
            "/tmp/one.sng",
            DateTimeOffset.UtcNow));

        var membership = await sut.GetMembershipMapAsync(LibrarySourceNames.Encore, ["md5-one", "md5-two"]);

        Assert.True(membership["md5-one"]);
        Assert.False(membership["md5-two"]);
    }

    [Fact]
    public async Task RemoveAsync_DeletesExistingEntry()
    {
        using var temp = new TemporaryDirectoryFixture("library-catalog-remove");
        var sut = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        await sut.UpsertAsync(new LibraryCatalogEntry(
            LibrarySourceNames.RhythmVerse,
            "song-123",
            "Song",
            "Artist",
            null,
            "/tmp/song.zip",
            DateTimeOffset.UtcNow));

        await sut.RemoveAsync(LibrarySourceNames.RhythmVerse, "song-123");

        var isPresent = await sut.IsInLibraryAsync(LibrarySourceNames.RhythmVerse, "song-123");

        Assert.False(isPresent);
    }

    [Fact]
    public async Task RemoveMissingLocalFilesAsync_RemovesEntriesWithMissingPaths()
    {
        using var temp = new TemporaryDirectoryFixture("library-catalog-reconcile");
        var sut = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        var existingFile = Path.Combine(temp.RootPath, "existing.sng");
        await File.WriteAllTextAsync(existingFile, "test");

        await sut.UpsertAsync(new LibraryCatalogEntry(
            LibrarySourceNames.Encore,
            "existing",
            "Existing",
            "Artist",
            null,
            existingFile,
            DateTimeOffset.UtcNow));

        await sut.UpsertAsync(new LibraryCatalogEntry(
            LibrarySourceNames.Encore,
            "missing",
            "Missing",
            "Artist",
            null,
            Path.Combine(temp.RootPath, "missing.sng"),
            DateTimeOffset.UtcNow));

        var removed = await sut.RemoveMissingLocalFilesAsync();

        Assert.Equal(1, removed);
        Assert.True(await sut.IsInLibraryAsync(LibrarySourceNames.Encore, "existing"));
        Assert.False(await sut.IsInLibraryAsync(LibrarySourceNames.Encore, "missing"));
    }

    [Fact]
    public async Task UpsertAsync_EnforcesUniqueLocalPathForInstalledLibraryRows()
    {
        using var temp = new TemporaryDirectoryFixture("library-catalog-unique-local-path");
        var sut = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var localPath = Path.Combine(temp.RootPath, "CloneHero", "Songs", "Artist", "Song", "Charter__encore");
        Directory.CreateDirectory(localPath);

        await sut.UpsertAsync(new LibraryCatalogEntry(
            LibrarySourceNames.RhythmVerse,
            LibraryIdentityService.BuildSourceKey(LibrarySourceNames.RhythmVerse, "rv-song"),
            "Song",
            "Artist",
            "Charter",
            localPath,
            DateTimeOffset.UtcNow.AddMinutes(-1)));

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () =>
            await sut.UpsertAsync(new LibraryCatalogEntry(
                LibrarySourceNames.Encore,
                LibraryIdentityService.BuildEncoreSourceKey(1, "md5-song"),
                "Song",
                "Artist",
                "Charter",
                localPath,
                DateTimeOffset.UtcNow)));
    }

    [Fact]
    public async Task RemoveDuplicateLocalPathEntriesUnderRootAsync_NoopsWhenSchemaAlreadyEnforcesUniquePaths()
    {
        using var temp = new TemporaryDirectoryFixture("library-catalog-root-dedupe-noop");
        var sut = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var cloneHeroPath = Path.Combine(temp.RootPath, "CloneHero", "Songs", "Artist", "Song", "Charter__encore");
        Directory.CreateDirectory(cloneHeroPath);

        await sut.UpsertAsync(new LibraryCatalogEntry(
            LibrarySourceNames.Encore,
            LibraryIdentityService.BuildEncoreSourceKey(42, "clonehero-new"),
            "Song",
            "Artist",
            "Charter",
            cloneHeroPath,
            DateTimeOffset.UtcNow));

        var removed = await sut.RemoveDuplicateLocalPathEntriesUnderRootAsync(Path.Combine(temp.RootPath, "CloneHero", "Songs"));

        Assert.Equal(0, removed);
        Assert.True(await sut.IsInLibraryAsync(LibrarySourceNames.Encore, LibraryIdentityService.BuildEncoreSourceKey(42, "clonehero-new")));
    }
}
