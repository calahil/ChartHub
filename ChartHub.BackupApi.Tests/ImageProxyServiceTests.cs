using System.Net;

using ChartHub.BackupApi.Options;
using ChartHub.BackupApi.Services;
using ChartHub.BackupApi.Tests.TestInfrastructure;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ImageProxyServiceTests : IDisposable
{
    private readonly string _tempRootPath = Path.Combine(Path.GetTempPath(), $"charthub-imageproxy-{Guid.NewGuid():N}");

    [Fact]
    public async Task GetImageAsync_CacheHit_ReturnsBytesWithoutHttpCall()
    {
        string expectedPath = Path.Combine(_tempRootPath, "cache", "images", "assets", "album_art", "cover.png");
        Directory.CreateDirectory(Path.GetDirectoryName(expectedPath)!);
        await File.WriteAllBytesAsync(expectedPath, [1, 2, 3]);

        int calls = 0;
        ImageProxyService sut = BuildService(
            (_, _) =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        ImageProxyResult? result = await sut.GetImageAsync("assets/album_art/cover.png", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal([1, 2, 3], result.Data);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task GetImageAsync_CacheMiss_FetchesUpstreamAndWritesToDisk()
    {
        int calls = 0;
        ImageProxyService sut = BuildService(
            (request, _) =>
            {
                calls++;
                Assert.Equal("https://rhythmverse.co/assets/album_art/cover.png", request.RequestUri?.ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([7, 8, 9]),
                });
            });

        ImageProxyResult? result = await sut.GetImageAsync("assets/album_art/cover.png", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal([7, 8, 9], result.Data);
        Assert.Equal(1, calls);

        string cachedPath = Path.Combine(_tempRootPath, "cache", "images", "assets", "album_art", "cover.png");
        Assert.True(File.Exists(cachedPath));
        Assert.Equal([7, 8, 9], await File.ReadAllBytesAsync(cachedPath));
    }

    [Theory]
    [InlineData("../img/cover.png")]
    [InlineData("img/../cover.png")]
    [InlineData("https://rhythmverse.co/assets/album_art/cover.png")]
    [InlineData("avatars")]
    public async Task GetImageAsync_InvalidPath_ReturnsNull(string path)
    {
        ImageProxyService sut = BuildService((_, _) => throw new InvalidOperationException("should not fetch"));

        ImageProxyResult? result = await sut.GetImageAsync(path, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetImageAsync_Upstream404_ReturnsNull()
    {
        ImageProxyService sut = BuildService(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        ImageProxyResult? result = await sut.GetImageAsync("avatars/author.png", CancellationToken.None);

        Assert.Null(result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRootPath))
        {
            Directory.Delete(_tempRootPath, recursive: true);
        }
    }

    private ImageProxyService BuildService(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        Directory.CreateDirectory(_tempRootPath);

        HttpClient client = new(new StubHttpMessageHandler(sendAsync));
        ImageCacheOptions cacheOptions = new()
        {
            CacheDirectory = Path.Combine("cache", "images"),
        };

        RhythmVerseSourceOptions sourceOptions = new()
        {
            BaseUrl = "https://rhythmverse.co/",
        };

        return new ImageProxyService(
            client,
            Microsoft.Extensions.Options.Options.Create(cacheOptions),
            Microsoft.Extensions.Options.Options.Create(sourceOptions),
            new TestHostEnvironment(_tempRootPath),
            NullLogger<ImageProxyService>.Instance);
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