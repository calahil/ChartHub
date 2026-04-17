using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Tests;

public sealed class GoogleDriveFolderArchiveServiceTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Precondition checks
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadFolderAsZipWhenApiKeyMissingThrows()
    {
        using var temp = new TempDirectory();
        GoogleDriveFolderArchiveService sut = BuildService(
            responses: [],
            apiKey: string.Empty);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.DownloadFolderAsZipAsync("folder-id", "my song", temp.Path, Guid.NewGuid(), CancellationToken.None));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Happy path — flat folder
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadFolderAsZipSingleFileCreatesZipWithOneEntry()
    {
        using var temp = new TempDirectory();
        var jobId = Guid.NewGuid();
        byte[] fileBytes = "hello world"u8.ToArray();

        List<(Func<Uri, bool> Match, HttpResponseMessage Response)> responses =
        [
            (uri => uri.AbsolutePath.Contains("/drive/v3/files") && uri.Query.Contains("q="),
                MakeListResponse([new DriveItem("file-1", "notes.txt", "text/plain")], nextPageToken: null)),
            (uri => uri.AbsolutePath.Contains("/drive/v3/files/file-1"),
                MakeBytesResponse(fileBytes)),
        ];

        GoogleDriveFolderArchiveService sut = BuildService(responses, apiKey: "test-key");
        string zipPath = await sut.DownloadFolderAsZipAsync("root-folder", "My Song", temp.Path, jobId, CancellationToken.None);

        Assert.True(File.Exists(zipPath));
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry entry = Assert.Single(zip.Entries);
        Assert.Equal("notes.txt", entry.FullName);
        await using Stream s = entry.Open();
        byte[] read = new byte[fileBytes.Length];
        int bytesRead = await s.ReadAsync(read);
        Assert.Equal(fileBytes.Length, bytesRead);
        Assert.Equal(fileBytes, read);
    }

    [Fact]
    public async Task DownloadFolderAsZipReturnsDestinationInsideDownloadsDirectory()
    {
        using var temp = new TempDirectory();
        var jobId = Guid.NewGuid();

        List<(Func<Uri, bool> Match, HttpResponseMessage Response)> responses =
        [
            (_ => true, MakeListResponse([new DriveItem("file-1", "track.mp3", "audio/mpeg")], nextPageToken: null)),
            (_ => true, MakeBytesResponse([])),
        ];

        GoogleDriveFolderArchiveService sut = BuildService(responses, apiKey: "test-key");
        string zipPath = await sut.DownloadFolderAsZipAsync("root-folder", "Track Song", temp.Path, jobId, CancellationToken.None);

        Assert.StartsWith(temp.Path, zipPath, StringComparison.Ordinal);
        Assert.EndsWith($"{jobId:D}.zip", zipPath, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Nested subfolder
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadFolderAsZipNestedFolderCreatesEntriesWithPrefix()
    {
        using var temp = new TempDirectory();
        var jobId = Guid.NewGuid();

        List<(Func<Uri, bool> Match, HttpResponseMessage Response)> responses =
        [
            (uri => uri.Query.Contains(Uri.EscapeDataString("'root-folder'")),
                MakeListResponse([new DriveItem("sub-folder-id", "Charts", "application/vnd.google-apps.folder")], nextPageToken: null)),
            (uri => uri.Query.Contains(Uri.EscapeDataString("'sub-folder-id'")),
                MakeListResponse([new DriveItem("file-2", "notes.chart", "text/plain")], nextPageToken: null)),
            (_ => true, MakeBytesResponse("chart data"u8.ToArray())),
        ];

        GoogleDriveFolderArchiveService sut = BuildService(responses, apiKey: "test-key");
        string zipPath = await sut.DownloadFolderAsZipAsync("root-folder", "Song Pack", temp.Path, jobId, CancellationToken.None);

        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry entry = Assert.Single(zip.Entries);
        Assert.Equal("Charts/notes.chart", entry.FullName);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Pagination
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadFolderAsZipPaginatedFolderCreatesAllEntries()
    {
        using var temp = new TempDirectory();
        var jobId = Guid.NewGuid();

        // Ordered stub: list page1 → download file-1 → list page2 → download file-2.
        // The service downloads each file immediately after listing it (inside the same
        // do-while iteration), so this is the actual dispatch order.
        List<(Func<Uri, bool> Match, HttpResponseMessage Response)> responses =
        [
            (_ => true, MakeListResponse([new DriveItem("file-1", "a.txt", "text/plain")], nextPageToken: "page2")),
            (_ => true, MakeBytesResponse([])),
            (_ => true, MakeListResponse([new DriveItem("file-2", "b.txt", "text/plain")], nextPageToken: null)),
            (_ => true, MakeBytesResponse([])),
        ];

        GoogleDriveFolderArchiveService sut = BuildService(responses, apiKey: "test-key");
        string zipPath = await sut.DownloadFolderAsZipAsync("root-folder", "Paged Song", temp.Path, jobId, CancellationToken.None);

        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(2, zip.Entries.Count);
        Assert.Contains(zip.Entries, e => e.FullName == "a.txt");
        Assert.Contains(zip.Entries, e => e.FullName == "b.txt");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Google Workspace file rejection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadFolderAsZipGoogleWorkspaceFileThrows()
    {
        using var temp = new TempDirectory();
        var jobId = Guid.NewGuid();

        List<(Func<Uri, bool> Match, HttpResponseMessage Response)> responses =
        [
            (_ => true, MakeListResponse(
                [new DriveItem("doc-1", "Pitch Deck", "application/vnd.google-apps.document")],
                nextPageToken: null)),
        ];

        GoogleDriveFolderArchiveService sut = BuildService(responses, apiKey: "test-key");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.DownloadFolderAsZipAsync("root-folder", "Bad Folder", temp.Path, jobId, CancellationToken.None));

        Assert.Contains("application/vnd.google-apps.document", ex.Message, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // File name sanitisation
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a/b", "a-b")]
    [InlineData("a\\b", "a-b")]
    public async Task DownloadFolderAsZipEntryNamesAreSanitised(string rawName, string expectedName)
    {
        using var temp = new TempDirectory();
        var jobId = Guid.NewGuid();

        List<(Func<Uri, bool> Match, HttpResponseMessage Response)> responses =
        [
            (_ => true, MakeListResponse([new DriveItem("file-x", rawName, "text/plain")], nextPageToken: null)),
            (_ => true, MakeBytesResponse([])),
        ];

        GoogleDriveFolderArchiveService sut = BuildService(responses, apiKey: "test-key");
        string zipPath = await sut.DownloadFolderAsZipAsync("root-folder", "Song", temp.Path, jobId, CancellationToken.None);

        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry entry = Assert.Single(zip.Entries);
        Assert.Equal(expectedName, entry.FullName);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // API key is embedded in HTTP requests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadFolderAsZipListRequestIncludesApiKey()
    {
        using var temp = new TempDirectory();
        var jobId = Guid.NewGuid();
        Uri? capturedListUri = null;

        List<(Func<Uri, bool> Match, HttpResponseMessage Response)> responses =
        [
            (uri =>
            {
                if (uri.Query.Contains("q="))
                {
                    capturedListUri = uri;
                }

                return uri.Query.Contains("q=");
            },
            MakeListResponse([], nextPageToken: null)),
        ];

        GoogleDriveFolderArchiveService sut = BuildService(responses, apiKey: "my-api-key");
        await sut.DownloadFolderAsZipAsync("root-folder", "Song", temp.Path, jobId, CancellationToken.None);

        Assert.NotNull(capturedListUri);
        Assert.Contains("my-api-key", capturedListUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadFolderAsZipDownloadRequestIncludesApiKey()
    {
        using var temp = new TempDirectory();
        var jobId = Guid.NewGuid();
        Uri? capturedDownloadUri = null;

        List<(Func<Uri, bool> Match, HttpResponseMessage Response)> responses =
        [
            (_ => true, MakeListResponse([new DriveItem("file-1", "track.ini", "text/plain")], nextPageToken: null)),
            (uri =>
            {
                capturedDownloadUri = uri;
                return true;
            },
            MakeBytesResponse([])),
        ];

        GoogleDriveFolderArchiveService sut = BuildService(responses, apiKey: "secret-key");
        await sut.DownloadFolderAsZipAsync("root-folder", "Song", temp.Path, jobId, CancellationToken.None);

        Assert.NotNull(capturedDownloadUri);
        Assert.Contains("secret-key", capturedDownloadUri!.Query, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Upstream HTTP failure
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadFolderAsZipListRequestNonSuccessThrows()
    {
        using var temp = new TempDirectory();
        var jobId = Guid.NewGuid();

        List<(Func<Uri, bool> Match, HttpResponseMessage Response)> responses =
        [
            (_ => true, new HttpResponseMessage(HttpStatusCode.Forbidden)),
        ];

        GoogleDriveFolderArchiveService sut = BuildService(responses, apiKey: "test-key");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.DownloadFolderAsZipAsync("root-folder", "Bad Auth Song", temp.Path, jobId, CancellationToken.None));
    }

    [Fact]
    public async Task DownloadFolderAsZipFileDownloadNonSuccessThrows()
    {
        using var temp = new TempDirectory();
        var jobId = Guid.NewGuid();

        List<(Func<Uri, bool> Match, HttpResponseMessage Response)> responses =
        [
            (_ => true, MakeListResponse([new DriveItem("file-1", "notes.txt", "text/plain")], nextPageToken: null)),
            (_ => true, new HttpResponseMessage(HttpStatusCode.NotFound)),
        ];

        GoogleDriveFolderArchiveService sut = BuildService(responses, apiKey: "test-key");

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.DownloadFolderAsZipAsync("root-folder", "Missing File Song", temp.Path, jobId, CancellationToken.None));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Cancellation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DownloadFolderAsZipCancelledBeforeListThrows()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        GoogleDriveFolderArchiveService sut = BuildService(responses: [], apiKey: "test-key");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.DownloadFolderAsZipAsync("root-folder", "Test Song", temp.Path, Guid.NewGuid(), cts.Token));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static GoogleDriveFolderArchiveService BuildService(
        IList<(Func<Uri, bool> Match, HttpResponseMessage Response)> responses,
        string apiKey)
    {
        int index = 0;
        SequencedHttpMessageHandler handler = new(request =>
        {
            Uri uri = request.RequestUri!;
            for (int i = index; i < responses.Count; i++)
            {
                if (responses[i].Match(uri))
                {
                    index = i + 1;
                    return Task.FromResult(responses[i].Response);
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"No stub matched {uri}"),
            });
        });

        HttpClient client = new(handler);
        StubHttpClientFactory factory = new(client);
        return new GoogleDriveFolderArchiveService(
            factory,
            Microsoft.Extensions.Options.Options.Create(new GoogleDriveOptions { ApiKey = apiKey }));
    }

    private static HttpResponseMessage MakeListResponse(
        IReadOnlyList<DriveItem> files,
        string? nextPageToken)
    {
        object payload = new
        {
            files = files.Select(f => new { f.Id, f.Name, f.MimeType }).ToArray(),
            nextPageToken,
        };

        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage MakeBytesResponse(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed record DriveItem(string Id, string Name, string MimeType);

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class SequencedHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return sendAsync(request);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "charthub-gdrive-archive-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
