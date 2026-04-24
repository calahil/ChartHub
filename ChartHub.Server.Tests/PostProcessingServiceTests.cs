using ChartHub.Conversion.Midi;
using ChartHub.Server.Contracts;
using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Tests;

public sealed class PostProcessingServiceTests
{
    [Fact]
    public void ApplyApprovedDrumResultWhenNotesMidExistsMergesIntoExistingMidi()
    {
        string root = Path.Combine(Path.GetTempPath(), $"charthub-postproc-test-{Guid.NewGuid():N}");

        try
        {
            string songDir = Path.Combine(root, "song");
            Directory.CreateDirectory(songDir);

            string notesMidPath = Path.Combine(songDir, "notes.mid");
            string generatedMidiPath = Path.Combine(root, "generated.mid");
            byte[] existing = [0x01, 0x02];
            byte[] generated = [0x03, 0x04];
            byte[] merged = [0x09, 0x08, 0x07];

            File.WriteAllBytes(notesMidPath, existing);
            File.WriteAllBytes(generatedMidiPath, generated);

            FakeCloneHeroLibraryService library = new(songDir);
            RecordingDrumMidiMerger merger = new(merged);
            PostProcessingService sut = CreateSut(root, library, merger);

            sut.ApplyApprovedDrumResult("song-1", generatedMidiPath);

            Assert.Equal(1, merger.CallCount);
            Assert.Equal(existing, merger.LastExistingMidi);
            Assert.Equal(generated, merger.LastGeneratedMidi);
            Assert.Equal(merged, File.ReadAllBytes(notesMidPath));
            Assert.True(Directory.Exists(Path.Combine(root + "-archive", "song-1")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            string archiveRoot = root + "-archive";
            if (Directory.Exists(archiveRoot))
            {
                Directory.Delete(archiveRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ApplyApprovedDrumResultWhenOnlyNotesChartExistsPreservesChartAndPromotesNotesMid()
    {
        string root = Path.Combine(Path.GetTempPath(), $"charthub-postproc-test-{Guid.NewGuid():N}");

        try
        {
            string songDir = Path.Combine(root, "song");
            Directory.CreateDirectory(songDir);

            string notesChartPath = Path.Combine(songDir, "notes.chart");
            string notesMidPath = Path.Combine(songDir, "notes.mid");
            string generatedMidiPath = Path.Combine(root, "generated.mid");
            byte[] chartBytes = [0x11, 0x22, 0x33];
            byte[] generated = [0x03, 0x04, 0x05];

            File.WriteAllBytes(notesChartPath, chartBytes);
            File.WriteAllBytes(generatedMidiPath, generated);

            FakeCloneHeroLibraryService library = new(songDir);
            RecordingDrumMidiMerger merger = new([0x99]);
            PostProcessingService sut = CreateSut(root, library, merger);

            sut.ApplyApprovedDrumResult("song-1", generatedMidiPath);

            Assert.Equal(0, merger.CallCount);
            Assert.Equal(chartBytes, File.ReadAllBytes(notesChartPath));
            Assert.Equal(generated, File.ReadAllBytes(notesMidPath));
            Assert.True(Directory.Exists(Path.Combine(root + "-archive", "song-1")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            string archiveRoot = root + "-archive";
            if (Directory.Exists(archiveRoot))
            {
                Directory.Delete(archiveRoot, recursive: true);
            }
        }
    }

    private static PostProcessingService CreateSut(string cloneHeroRoot, ICloneHeroLibraryService library, IDrumMidiMerger merger)
    {
        IOptions<ServerPathOptions> options = Microsoft.Extensions.Options.Options.Create(new ServerPathOptions
        {
            CloneHeroRoot = cloneHeroRoot,
        });

        return new PostProcessingService(options, library, merger, NullLogger<PostProcessingService>.Instance);
    }

    private sealed class FakeCloneHeroLibraryService : ICloneHeroLibraryService
    {
        private readonly CloneHeroSongResponse _song;

        public FakeCloneHeroLibraryService(string installedPath)
        {
            _song = new CloneHeroSongResponse
            {
                SongId = "song-1",
                Source = "test",
                SourceId = "test-id",
                Artist = "artist",
                Title = "title",
                Charter = "charter",
                InstalledPath = installedPath,
                InstalledRelativePath = "song",
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        public IReadOnlyList<CloneHeroSongResponse> ListSongs()
        {
            return [_song];
        }

        public bool TryGetSong(string songId, out CloneHeroSongResponse? song)
        {
            if (string.Equals(songId, _song.SongId, StringComparison.Ordinal))
            {
                song = _song;
                return true;
            }

            song = null;
            return false;
        }

        public bool TrySoftDeleteSong(string songId, out CloneHeroSongResponse? song)
        {
            song = null;
            return false;
        }

        public bool TryRestoreSong(string songId, out CloneHeroSongResponse? song)
        {
            song = null;
            return false;
        }

        public void UpsertInstalledSong(CloneHeroLibraryUpsertRequest request)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingDrumMidiMerger(byte[] mergedBytes) : IDrumMidiMerger
    {
        public int CallCount { get; private set; }

        public byte[] LastExistingMidi { get; private set; } = [];

        public byte[] LastGeneratedMidi { get; private set; } = [];

        public byte[] MergeGeneratedDrums(byte[] existingMidi, byte[] gmDrumMidi)
        {
            CallCount++;
            LastExistingMidi = existingMidi;
            LastGeneratedMidi = gmDrumMidi;
            return mergedBytes;
        }
    }
}
