using System.Net;
using System.Reflection;
using System.Text;

using ChartHub.Models;
using ChartHub.Services;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public sealed class DesktopSyncApiClientTests
{
    [Fact]
    public void ParseBaseUri_WithEmptyInput_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => InvokeParseBaseUri(" "));
        Assert.Equal("Desktop API URL is required.", ex.Message);
    }

    [Fact]
    public void ParseBaseUri_WithoutScheme_DefaultsToHttp()
    {
        Uri result = InvokeParseBaseUri("localhost:5005");

        Assert.Equal("http", result.Scheme);
        Assert.Equal("localhost", result.Host);
    }

    [Fact]
    public void ParseBaseUri_WithHttps_PreservesHttps()
    {
        Uri result = InvokeParseBaseUri("https://example.com");

        Assert.Equal("https", result.Scheme);
        Assert.Equal("example.com", result.Host);
    }

    [Fact]
    public void ParseBaseUri_WithInvalidScheme_Throws()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => InvokeParseBaseUri("http://"));
        Assert.Equal("Desktop API URL is invalid.", ex.Message);
    }

    [Fact]
    public async Task TryReadErrorAsync_WithJsonError_ReturnsMessage()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"pair code expired\"}", Encoding.UTF8, "application/json"),
        };

        string result = await InvokeTryReadErrorAsync(response);

        Assert.Equal("pair code expired", result);
    }

    [Fact]
    public async Task TryReadErrorAsync_WithPlainText_ReturnsRawPayload()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("plain failure", Encoding.UTF8, "text/plain"),
        };

        string result = await InvokeTryReadErrorAsync(response);

        Assert.Equal("plain failure", result);
    }

    [Fact]
    public async Task TryReadErrorAsync_WithEmptyPayload_ReturnsDefaultMessage()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
        };

        string result = await InvokeTryReadErrorAsync(response);

        Assert.Equal("No error payload.", result);
    }

    [Fact]
    public async Task ReadJsonAsync_WithNullPayload_Throws()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json"),
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeReadJsonAsync<DesktopSyncVersionResponse>(response));
        Assert.Equal("Desktop sync API returned an empty payload.", ex.Message);
    }

    [Fact]
    public void MapQueueItem_WithInvalidEnumAndDate_FallsBackToDefaults()
    {
        Type clientType = typeof(DesktopSyncApiClient);
        Type envelopeType = clientType.GetNestedType("IngestionQueueItemEnvelope", BindingFlags.NonPublic)!;
        object envelope = Activator.CreateInstance(envelopeType)!;

        envelopeType.GetProperty("IngestionId")!.SetValue(envelope, 42L);
        envelopeType.GetProperty("CurrentState")!.SetValue(envelope, "not-a-state");
        envelopeType.GetProperty("DesktopState")!.SetValue(envelope, "bad-desktop-state");
        envelopeType.GetProperty("UpdatedAtUtc")!.SetValue(envelope, "not-a-date");
        envelopeType.GetProperty("DisplayName")!.SetValue(envelope, null);

        MethodInfo mapMethod = clientType.GetMethod("MapQueueItem", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (IngestionQueueItem)mapMethod.Invoke(null, [envelope])!;

        Assert.Equal(42L, result.IngestionId);
        Assert.Equal(default, result.CurrentState);
        Assert.Equal(default, result.DesktopState);
        Assert.Equal(default, result.UpdatedAtUtc);
        Assert.Equal("Ingestion 42", result.DisplayName);
    }

    [Fact]
    public void MapQueueItem_WithValidValues_MapsParsedValues()
    {
        Type clientType = typeof(DesktopSyncApiClient);
        Type envelopeType = clientType.GetNestedType("IngestionQueueItemEnvelope", BindingFlags.NonPublic)!;
        object envelope = Activator.CreateInstance(envelopeType)!;

        envelopeType.GetProperty("IngestionId")!.SetValue(envelope, 7L);
        envelopeType.GetProperty("CurrentState")!.SetValue(envelope, "Queued");
        envelopeType.GetProperty("DesktopState")!.SetValue(envelope, "Installed");
        envelopeType.GetProperty("UpdatedAtUtc")!.SetValue(envelope, "2026-01-02T03:04:05Z");
        envelopeType.GetProperty("DisplayName")!.SetValue(envelope, "Song 7");
        envelopeType.GetProperty("Source")!.SetValue(envelope, "Encore");
        envelopeType.GetProperty("SourceLink")!.SetValue(envelope, "https://encore.example/song7");

        MethodInfo mapMethod = clientType.GetMethod("MapQueueItem", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (IngestionQueueItem)mapMethod.Invoke(null, [envelope])!;

        Assert.Equal(7L, result.IngestionId);
        Assert.Equal(IngestionState.Queued, result.CurrentState);
        Assert.Equal(DesktopState.Installed, result.DesktopState);
        Assert.Equal("Song 7", result.DisplayName);
        Assert.Equal("Encore", result.Source);
        Assert.Equal("https://encore.example/song7", result.SourceLink);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), result.UpdatedAtUtc);
    }

    private static Uri InvokeParseBaseUri(string value)
    {
        MethodInfo method = typeof(DesktopSyncApiClient).GetMethod("ParseBaseUri", BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            return (Uri)method.Invoke(null, [value])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException inner)
        {
            throw inner;
        }
    }

    private static async Task<string> InvokeTryReadErrorAsync(HttpResponseMessage response)
    {
        MethodInfo method = typeof(DesktopSyncApiClient).GetMethod("TryReadErrorAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task<string>)method.Invoke(null, [response, CancellationToken.None])!;
        return await task;
    }

    private static async Task<T> InvokeReadJsonAsync<T>(HttpResponseMessage response)
    {
        MethodInfo generic = typeof(DesktopSyncApiClient).GetMethod("ReadJsonAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo method = generic.MakeGenericMethod(typeof(T));

        try
        {
            var task = (Task<T>)method.Invoke(null, [response, CancellationToken.None])!;
            return await task;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException inner)
        {
            throw inner;
        }
    }
}
