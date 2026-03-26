using System.Net;

using ChartHub.BackupApi.Options;
using ChartHub.BackupApi.Services;
using ChartHub.BackupApi.Tests.TestInfrastructure;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class DownloadProxyServiceTests : IDisposable
{
    private readonly string _tempRootPath = Path.Combine(Path.GetTempPath(), $"charthub-downloadproxy-{Guid.NewGuid():N}");

    [Fact]
    public async Task GetDownloadFileAsync_CacheHit_ReturnsFileWithoutHttpCall()
    {
        string expectedPath = Path.Combine(_tempRootPath, "cache", "downloads", "download_file", "stackoverflow", "abc123", "file.rar");
        Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);
        await File.WriteAllBytesAsync(expectedPath, [1, 2, 3]);

        int calls = 0;
        DownloadProxyService sut = BuildService(
            (_, _) =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        DownloadProxyResult? result = await sut.GetDownloadFileAsync("download_file/stackoverflow/abc123/file.rar", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(expectedPath, result.FilePath);
        Assert.Equal("application/vnd.rar", result.ContentType);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task GetDownloadFileAsync_CacheMiss_FetchesUpstreamAndWritesToDisk()
    {
        int calls = 0;
        DownloadProxyService sut = BuildService(
            (request, _) =>
            {
                calls++;
                Assert.Equal("https://rhythmverse.co/download_file/stackoverflow/abc123/file.rar", request.RequestUri?.ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([7, 8, 9]),
                });
            });

        DownloadProxyResult? result = await sut.GetDownloadFileAsync("download_file/stackoverflow/abc123/file.rar", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, calls);
        Assert.True(File.Exists(result.FilePath));
        Assert.Equal([7, 8, 9], await File.ReadAllBytesAsync(result.FilePath));
        Assert.Equal("application/vnd.rar", result.ContentType);
    }

    [Theory]
    [InlineData("../download_file/a/b/file.rar")]
    [InlineData("download_file/../a/b/file.rar")]
    [InlineData("https://rhythmverse.co/download_file/a/b/file.rar")]
    [InlineData("img/a/b.png")]
    [InlineData("download_file")]
    public async Task GetDownloadFileAsync_InvalidPath_ReturnsNull(string path)
    {
        DownloadProxyService sut = BuildService((_, _) => throw new InvalidOperationException("should not fetch"));

        DownloadProxyResult? result = await sut.GetDownloadFileAsync(path, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDownloadFileAsync_Upstream404_ReturnsNull()
    {
        DownloadProxyService sut = BuildService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        DownloadProxyResult? result = await sut.GetDownloadFileAsync("download_file/stackoverflow/abc123/file.rar", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetExternalDownloadAsync_DirectHttp_CacheMiss_FetchesAndWritesToDisk()
    {
        int calls = 0;
        DownloadProxyService sut = BuildService(
            (request, _) =>
            {
                calls++;
                Assert.Equal("https://cdn.example/live-song.zip", request.RequestUri?.ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([4, 5, 6]),
                    RequestMessage = request,
                });
            });

        DownloadProxyResult? result = await sut.GetExternalDownloadAsync("https://cdn.example/live-song.zip", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, calls);
        Assert.True(File.Exists(result.FilePath));
        Assert.Equal([4, 5, 6], await File.ReadAllBytesAsync(result.FilePath));
        Assert.Equal("application/zip", result.ContentType);
    }

    [Fact]
    public async Task GetExternalDownloadAsync_MediaFire_ResolvesLandingPageAndDownloadsFile()
    {
        int calls = 0;
        DownloadProxyService sut = BuildService(
            (request, _) =>
            {
                calls++;

                if (request.RequestUri?.ToString() == "https://www.mediafire.com/file/abc123/demo")
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("<html><body><a id=\"downloadButton\" href=\"https://download239.mediafire.com/demo/file.zip\">download</a></body></html>"),
                        RequestMessage = request,
                    });
                }

                Assert.Equal("https://download239.mediafire.com/demo/file.zip", request.RequestUri?.ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([8, 9, 10]),
                    RequestMessage = request,
                });
            });

        DownloadProxyResult? result = await sut.GetExternalDownloadAsync("https://www.mediafire.com/file/abc123/demo", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, calls);
        Assert.True(File.Exists(result.FilePath));
        Assert.Equal([8, 9, 10], await File.ReadAllBytesAsync(result.FilePath));
        Assert.Equal("application/zip", result.ContentType);
    }

    [Theory]
    [InlineData("https://drive.google.com/file/d/abc/view")]
    [InlineData("http://127.0.0.1:5147/download_file/a/b/c.zip")]
    [InlineData("not-a-url")]
    public async Task GetExternalDownloadAsync_UnsupportedSource_ReturnsNull(string sourceUrl)
    {
        DownloadProxyService sut = BuildService((_, _) => throw new InvalidOperationException("should not fetch"));

        DownloadProxyResult? result = await sut.GetExternalDownloadAsync(sourceUrl, CancellationToken.None);

        Assert.Null(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRootPath))
        {
            Directory.Delete(_tempRootPath, recursive: true);
        }
    }

    private DownloadProxyService BuildService(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        Directory.CreateDirectory(_tempRootPath);

        HttpClient client = new(new StubHttpMessageHandler(sendAsync));
        DownloadOptions downloadOptions = new()
        {
            CacheDirectory = Path.Combine("cache", "downloads"),
        };

        RhythmVerseSourceOptions sourceOptions = new()
        {
            BaseUrl = "https://rhythmverse.co/",
        };

        return new DownloadProxyService(
            client,
            Microsoft.Extensions.Options.Options.Create(downloadOptions),
            Microsoft.Extensions.Options.Options.Create(sourceOptions),
            new TestHostEnvironment(_tempRootPath),
            NullLogger<DownloadProxyService>.Instance);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => sendAsync(request, cancellationToken);
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "ChartHub.BackupApi.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}