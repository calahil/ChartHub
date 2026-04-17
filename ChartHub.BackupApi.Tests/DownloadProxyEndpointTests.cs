using ChartHub.BackupApi.Services;
using ChartHub.BackupApi.Tests.TestInfrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public sealed class DownloadProxyEndpointTests : IClassFixture<BackupApiWebApplicationFactory>, IDisposable
{
    private readonly BackupApiWebApplicationFactory _factory;
    private readonly string _tempRootPath = Path.Combine(Path.GetTempPath(), $"charthub-download-endpoint-{Guid.NewGuid():N}");

    public DownloadProxyEndpointTests(BackupApiWebApplicationFactory factory)
    {
        _factory = factory;
        Directory.CreateDirectory(_tempRootPath);
    }

    [Fact]
    public async Task GetDownloadFile_WhenProxyReturnsFile_RespondsWithFilePayload()
    {
        string payloadPath = Path.Combine(_tempRootPath, "TrailerTrash_wii.rar");
        await File.WriteAllBytesAsync(payloadPath, [10, 11, 12]);

        using HttpClient client = _factory
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDownloadProxyService>(new StubDownloadProxyService(
                    new DownloadProxyResult(payloadPath, "application/vnd.rar")));
            }))
            .CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/download_file/stackoverflow/532e3cb651aa6/TrailerTrash_wii.rar");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/vnd.rar", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal([10, 11, 12], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task GetDownloadFile_WhenProxyMisses_Returns404()
    {
        using HttpClient client = _factory
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDownloadProxyService>(new StubDownloadProxyService(null));
            }))
            .CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/download_file/stackoverflow/532e3cb651aa6/TrailerTrash_wii.rar");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HeadDownloadFile_WhenProxyReturnsFile_DoesNotReturnMethodNotAllowed()
    {
        string payloadPath = Path.Combine(_tempRootPath, "head-download-file.rar");
        await File.WriteAllBytesAsync(payloadPath, [31, 32, 33]);

        using HttpClient client = _factory
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDownloadProxyService>(new StubDownloadProxyService(
                    new DownloadProxyResult(payloadPath, "application/vnd.rar")));
            }))
            .CreateAuthenticatedClient();

        using HttpRequestMessage request = new(HttpMethod.Head, "/download_file/stackoverflow/532e3cb651aa6/head-download-file.rar");
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.NotEqual(System.Net.HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HeadDownloadFile_WhenProxyMisses_Returns404()
    {
        using HttpClient client = _factory
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDownloadProxyService>(new StubDownloadProxyService(null));
            }))
            .CreateAuthenticatedClient();

        using HttpRequestMessage request = new(HttpMethod.Head, "/download_file/stackoverflow/532e3cb651aa6/head-miss.rar");
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetExternalDownload_WhenProxyReturnsFile_RespondsWithFilePayload()
    {
        string payloadPath = Path.Combine(_tempRootPath, "external-song.zip");
        await File.WriteAllBytesAsync(payloadPath, [20, 21, 22]);

        using HttpClient client = _factory
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDownloadProxyService>(new StubDownloadProxyService(
                    fileResult: null,
                    externalResult: new DownloadProxyResult(payloadPath, "application/zip")));
            }))
            .CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/downloads/external?sourceUrl=https%3A%2F%2Fcdn.example%2Flive-song.zip");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal([20, 21, 22], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task GetExternalDownload_WhenProxyMisses_Returns404()
    {
        using HttpClient client = _factory
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDownloadProxyService>(new StubDownloadProxyService(
                    fileResult: null,
                    externalResult: null));
            }))
            .CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync("/downloads/external?sourceUrl=https%3A%2F%2Fcdn.example%2Flive-song.zip");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HeadExternalDownload_WhenProxyReturnsFile_DoesNotReturnMethodNotAllowed()
    {
        string payloadPath = Path.Combine(_tempRootPath, "head-external.zip");
        await File.WriteAllBytesAsync(payloadPath, [41, 42, 43]);

        using HttpClient client = _factory
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDownloadProxyService>(new StubDownloadProxyService(
                    fileResult: null,
                    externalResult: new DownloadProxyResult(payloadPath, "application/zip")));
            }))
            .CreateAuthenticatedClient();

        using HttpRequestMessage request = new(HttpMethod.Head, "/downloads/external?sourceUrl=https%3A%2F%2Fcdn.example%2Flive-song.zip");
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.NotEqual(System.Net.HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HeadExternalDownload_WhenProxyMisses_Returns404()
    {
        using HttpClient client = _factory
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDownloadProxyService>(new StubDownloadProxyService(
                    fileResult: null,
                    externalResult: null));
            }))
            .CreateAuthenticatedClient();

        using HttpRequestMessage request = new(HttpMethod.Head, "/downloads/external?sourceUrl=https%3A%2F%2Fcdn.example%2Flive-song.zip");
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRootPath))
        {
            Directory.Delete(_tempRootPath, recursive: true);
        }
    }

    private sealed class StubDownloadProxyService(DownloadProxyResult? fileResult, DownloadProxyResult? externalResult = null) : IDownloadProxyService
    {
        public Task<DownloadProxyResult?> GetDownloadFileAsync(string downloadPath, CancellationToken cancellationToken)
            => Task.FromResult(fileResult);

        public Task<DownloadProxyResult?> GetExternalDownloadAsync(string sourceUrl, CancellationToken cancellationToken)
            => Task.FromResult(externalResult);
    }
}