using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ChartHub.Hud.Services;

public sealed class ServerVolumeService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _volumeStreamUrl;

    public ServerVolumeService(int serverPort)
    {
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        _volumeStreamUrl = $"http://localhost:{serverPort}/api/v1/hud/volume/stream";
    }

    public async IAsyncEnumerable<HudVolumeStatus> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        const int MaxBackoffSeconds = 30;

        while (!cancellationToken.IsCancellationRequested)
        {
            await foreach (HudVolumeStatus status in ConnectAndReadAsync(cancellationToken).ConfigureAwait(false))
            {
                backoff = TimeSpan.FromSeconds(1);
                yield return status;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

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

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async IAsyncEnumerable<HudVolumeStatus> ConnectAndReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        Stream? stream = null;
        StreamReader? reader = null;

        try
        {
            response = await _httpClient
                .GetAsync(_volumeStreamUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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
            await foreach (HudVolumeStatus status in ReadSseEventsAsync(reader, cancellationToken).ConfigureAwait(false))
            {
                yield return status;
            }
        }
    }

    private static async IAsyncEnumerable<HudVolumeStatus> ReadSseEventsAsync(
        StreamReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? dataLine = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                dataLine = line["data: ".Length..];
            }
            else if (line.Length == 0 && dataLine is not null)
            {
                HudVolumeStatus? parsed = ParseVolumeStatus(dataLine);
                if (parsed.HasValue)
                {
                    yield return parsed.Value;
                }

                dataLine = null;
            }
        }
    }

    private static HudVolumeStatus? ParseVolumeStatus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("isAvailable", out JsonElement availableElement)
                || !root.TryGetProperty("valuePercent", out JsonElement valueElement)
                || !root.TryGetProperty("isMuted", out JsonElement mutedElement))
            {
                return null;
            }

            bool isAvailable = availableElement.GetBoolean();
            int valuePercent = Math.Clamp(valueElement.GetInt32(), 0, 100);
            bool isMuted = mutedElement.GetBoolean();

            return new HudVolumeStatus(isAvailable, valuePercent, isMuted);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public readonly record struct HudVolumeStatus(bool IsAvailable, int ValuePercent, bool IsMuted);