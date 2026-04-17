using ChartHub.BackupApi.Services;
using ChartHub.BackupApi.Tests.TestInfrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public sealed class ImageProxyEndpointTests : IClassFixture<BackupApiWebApplicationFactory>
{
    private readonly BackupApiWebApplicationFactory _factory;

    public ImageProxyEndpointTests(BackupApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAlbumArt_WhenProxyReturnsBytes_RespondsWithImagePayload()
    {
        using HttpClient client = _factory
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddSingleton<IImageProxyService>(new StubImageProxyService(new ImageProxyResult([4, 5, 6], "image/png")));
            }))
            .CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/assets/album_art/data-art.png");

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal([4, 5, 6], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task GetAvatar_WhenProxyMisses_Returns404()
    {
        using HttpClient client = _factory
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddSingleton<IImageProxyService>(new StubImageProxyService(null));
            }))
            .CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/avatars/author.png");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class StubImageProxyService(ImageProxyResult? result) : IImageProxyService
    {
        public Task<ImageProxyResult?> GetImageAsync(string imagePath, CancellationToken cancellationToken)
            => Task.FromResult(result);
    }
}