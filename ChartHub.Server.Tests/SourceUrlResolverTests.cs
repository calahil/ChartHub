using System.Net;

using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Tests;

public sealed class SourceUrlResolverTests
{
    [Fact]
    public async Task ResolveAsyncNonDriveUrlReturnsOriginalUrl()
    {
        SourceUrlResolver sut = BuildResolver((_, _) => throw new InvalidOperationException("should not call upstream"), apiKey: "k");

        ResolvedSourceUrl result = await sut.ResolveAsync("https://example.com/demo.zip", CancellationToken.None);

        Assert.Equal("https://example.com/demo.zip", result.DownloadUri.ToString());
        Assert.Null(result.SuggestedName);
    }

    [Fact]
    public async Task ResolveAsyncDriveFileUrlUsesMetadataThenMediaUrl()
    {
        int calls = 0;
        SourceUrlResolver sut = BuildResolver(
            (request, _) =>
            {
                calls++;
                string uri = request.RequestUri!.ToString();
                if (uri.StartsWith("https://www.googleapis.com/drive/v3/files/abc123?fields=id,name,mimeType&key=test-key", StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"id\":\"abc123\",\"name\":\"track.zip\",\"mimeType\":\"application/zip\"}"),
                    });
                }

                throw new InvalidOperationException($"unexpected call: {uri}");
            },
            apiKey: "test-key");

        ResolvedSourceUrl result = await sut.ResolveAsync("https://drive.google.com/file/d/abc123/view?usp=sharing", CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal("https://www.googleapis.com/drive/v3/files/abc123?alt=media&key=test-key", result.DownloadUri.ToString());
        Assert.Equal("track.zip", result.SuggestedName);
    }

    [Fact]
    public async Task ResolveAsyncDriveFolderThrowsNotSupported()
    {
        SourceUrlResolver sut = BuildResolver(
            (_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":\"folder1\",\"name\":\"Folder\",\"mimeType\":\"application/vnd.google-apps.folder\"}"),
                }),
            apiKey: "test-key");

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            sut.ResolveAsync("https://drive.google.com/file/d/folder1/view", CancellationToken.None));
    }

    [Theory]
    [InlineData("https://drive.google.com/file/d/abc123/view", "abc123")]
    [InlineData("https://drive.google.com/open?id=qwe987", "qwe987")]
    [InlineData("https://example.com/open?id=qwe987", null)]
    public void TryExtractGoogleDriveFileIdParsesExpectedIds(string sourceUrl, string? expected)
    {
        Uri uri = new(sourceUrl);

        string? fileId = SourceUrlResolver.TryExtractGoogleDriveFileId(uri);

        Assert.Equal(expected, fileId);
    }

    private static SourceUrlResolver BuildResolver(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        string apiKey)
    {
        StubHttpMessageHandler handler = new(sendAsync);
        HttpClient client = new(handler);
        StubHttpClientFactory clientFactory = new(client);

        return new SourceUrlResolver(
            clientFactory,
            Microsoft.Extensions.Options.Options.Create(new GoogleDriveOptions { ApiKey = apiKey }));
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => sendAsync(request, cancellationToken);
    }
}
