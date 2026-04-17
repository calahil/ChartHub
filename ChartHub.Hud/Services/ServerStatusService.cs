using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ChartHub.Hud.Services;

/// <summary>
/// Connects to ChartHub.Server's loopback SSE endpoint and streams
/// connected-device-count updates. Reconnects with exponential backoff
/// if the server is not yet available or if the connection drops.
/// </summary>
public sealed class ServerStatusService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _statusStreamUrl;

    public ServerStatusService(int serverPort)
    {
        _httpClient = new HttpClient
        {
            // Long-lived SSE connection — ensure no short timeout.
            Timeout = Timeout.InfiniteTimeSpan,
        };

        _statusStreamUrl = $"http://localhost:{serverPort}/api/v1/hud/status/stream";
    }

    public async IAsyncEnumerable<int> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        const int MaxBackoffSeconds = 30;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Drain any counts collected during a connected session.
            await foreach (int count in ConnectAndReadAsync(cancellationToken).ConfigureAwait(false))
            {
                backoff = TimeSpan.FromSeconds(1);
                yield return count;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            // Back off before retrying.
            try
            {
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            if (backoff.TotalSeconds < MaxBackoffSeconds)
            {
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, MaxBackoffSeconds));
            }
        }
    }

    private async IAsyncEnumerable<int> ConnectAndReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        Stream? stream = null;
        StreamReader? reader = null;

        try
        {
            response = await _httpClient
                .GetAsync(_statusStreamUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            reader = new StreamReader(stream);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            reader?.Dispose();
            stream?.Dispose();
            response?.Dispose();
            yield break;
        }
        catch
        {
            reader?.Dispose();
            stream?.Dispose();
            response?.Dispose();
            yield break;
        }

        using (response)
        using (stream)
        using (reader)
        {
            await foreach (int count in ReadSseEventsAsync(reader, cancellationToken).ConfigureAwait(false))
            {
                yield return count;
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static async IAsyncEnumerable<int> ReadSseEventsAsync(
        StreamReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? dataLine = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line is null)
            {
                // Stream ended.
                yield break;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                dataLine = line["data: ".Length..];
            }
            else if (line.Length == 0 && dataLine is not null)
            {
                // Empty line = end of SSE event.
                int? count = ParseCount(dataLine);
                if (count.HasValue)
                {
                    yield return count.Value;
                }

                dataLine = null;
            }
        }
    }

    private static int? ParseCount(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("connectedDeviceCount", out JsonElement el)
                && el.TryGetInt32(out int count))
            {
                return count;
            }
        }
        catch (JsonException)
        {
            // Malformed event — ignore.
        }

        return null;
    }
}
