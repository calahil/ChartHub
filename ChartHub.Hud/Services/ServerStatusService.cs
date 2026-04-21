using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ChartHub.Hud.Services;

/// <summary>
/// Connects to ChartHub.Server's loopback SSE endpoint and streams
/// Hud status updates. Reconnects with exponential backoff
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

    public async IAsyncEnumerable<HudStatus> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        const int MaxBackoffSeconds = 30;

        while (!cancellationToken.IsCancellationRequested)
        {
            await foreach (HudStatus status in ConnectAndReadAsync(cancellationToken).ConfigureAwait(false))
            {
                backoff = TimeSpan.FromSeconds(1);
                yield return status;
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

    private async IAsyncEnumerable<HudStatus> ConnectAndReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
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
            await foreach (HudStatus status in ReadSseEventsAsync(reader, cancellationToken).ConfigureAwait(false))
            {
                yield return status;
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static async IAsyncEnumerable<HudStatus> ReadSseEventsAsync(
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
                HudStatus? status = ParseStatus(dataLine);
                if (status.HasValue)
                {
                    yield return status.Value;
                }

                dataLine = null;
            }
        }
    }

    private static HudStatus? ParseStatus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("isPresent", out JsonElement isPresentEl))
            {
                return null;
            }

            bool isPresent = isPresentEl.GetBoolean();

            string? deviceName = null;
            if (root.TryGetProperty("deviceName", out JsonElement nameEl)
                && nameEl.ValueKind == JsonValueKind.String)
            {
                deviceName = nameEl.GetString();
            }

            string? userEmail = null;
            if (root.TryGetProperty("userEmail", out JsonElement emailEl)
                && emailEl.ValueKind == JsonValueKind.String)
            {
                userEmail = emailEl.GetString();
            }

            bool uinputAvailable = true;
            if (root.TryGetProperty("uinputAvailable", out JsonElement uinputEl))
            {
                uinputAvailable = uinputEl.GetBoolean();
            }

            return new HudStatus(isPresent, deviceName, userEmail, uinputAvailable);
        }
        catch (JsonException)
        {
            // Malformed event — ignore.
        }

        return null;
    }
}

public readonly record struct HudStatus(bool IsPresent, string? DeviceName, string? UserEmail, bool UinputAvailable);
