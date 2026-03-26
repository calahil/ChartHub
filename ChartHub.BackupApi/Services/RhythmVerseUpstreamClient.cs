using System.Globalization;
using System.Text.Json;

using ChartHub.BackupApi.Models;
using ChartHub.BackupApi.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.BackupApi.Services;

public sealed class RhythmVerseUpstreamClient(
    HttpClient httpClient,
    IOptions<RhythmVerseSourceOptions> sourceOptions) : IRhythmVerseUpstreamClient
{
    public async Task<RhythmVersePageEnvelope> FetchSongsPageAsync(int page, int records, long? updatedSince, CancellationToken cancellationToken)
    {
        string baseUrl = sourceOptions.Value.BaseUrl.TrimEnd('/');
        string path = sourceOptions.Value.SongsPath.TrimStart('/');
        var requestUri = new Uri($"{baseUrl}/{path}");

        MultipartFormDataContent content = BuildSongsContent(page, records);

        using HttpRequestMessage request = new(HttpMethod.Post, requestUri) { Content = content };

        if (!string.IsNullOrWhiteSpace(sourceOptions.Value.Token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sourceOptions.Value.Token);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Log status code before throwing to aid diagnostics
        if (!response.IsSuccessStatusCode)
        {
            string responseBody = string.Empty;
            try
            {
                responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (responseBody.Length > 200)
                {
                    responseBody = string.Concat(responseBody.AsSpan(0, 200), "...");
                }
            }
            catch
            {
                // Ignore errors reading response body for logging
            }
        }

        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        JsonElement root = document.RootElement;
        JsonElement dataElement = root.GetProperty("data");
        JsonElement recordsElement = dataElement.GetProperty("records");
        JsonElement paginationElement = dataElement.GetProperty("pagination");

        List<System.Text.Json.Nodes.JsonNode?> songs = new();
        foreach (JsonElement song in dataElement.GetProperty("songs").EnumerateArray())
        {
            songs.Add(System.Text.Json.Nodes.JsonNode.Parse(song.GetRawText()));
        }

        return new RhythmVersePageEnvelope
        {
            TotalAvailable = ReadInt(recordsElement, "total_available"),
            TotalFiltered = ReadInt(recordsElement, "total_filtered"),
            Returned = ReadInt(recordsElement, "returned"),
            Start = ReadInt(paginationElement, "start"),
            Records = ReadInt(paginationElement, "records"),
            Page = ReadInt(paginationElement, "page"),
            Songs = songs,
        };
    }

    public IReadOnlyList<SyncedSong> ConvertToSyncedSongs(RhythmVersePageEnvelope envelope)
    {
        List<SyncedSong> songs = new(envelope.Songs.Count);

        foreach (System.Text.Json.Nodes.JsonNode? songNode in envelope.Songs)
        {
            if (songNode is null)
            {
                continue;
            }

            using var songDocument = JsonDocument.Parse(songNode.ToJsonString());
            JsonElement songElement = songDocument.RootElement;
            if (songElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!songElement.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!songElement.TryGetProperty("file", out JsonElement file)
                || file.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            songs.Add(new SyncedSong
            {
                SongId = ReadLong(data, "song_id"),
                RecordId = ReadNullableString(data, "record_id"),
                Artist = ReadString(data, "artist"),
                Title = ReadString(data, "title"),
                Album = ReadString(data, "album"),
                Genre = ReadString(data, "genre"),
                Year = ReadNullableInt(data, "year"),
                RecordUpdatedUnix = ReadNullableLong(data, "record_updated"),
                FileId = ReadString(file, "file_id"),
                DownloadUrl = ResolveDownloadUrl(file),
                DiffGuitar = ReadNullableInt(data, "diff_guitar"),
                DiffBass = ReadNullableInt(data, "diff_bass"),
                DiffDrums = ReadNullableInt(data, "diff_drums"),
                DiffVocals = ReadNullableInt(data, "diff_vocals"),
                DiffKeys = ReadNullableInt(data, "diff_keys"),
                DiffBand = ReadNullableInt(data, "diff_band"),
                AuthorId = ReadString(file, "author_id"),
                GroupId = ReadString(file, "group_id"),
                GameFormat = ReadString(file, "gameformat"),
                SongJson = songNode.ToJsonString(),
                DataJson = data.GetRawText(),
                FileJson = file.GetRawText(),
            });
        }

        return songs;
    }

    /// <summary>
    /// Builds the POST form content for the RhythmVerse API list endpoint.
    /// The endpoint requires multipart form data with pagination and sort parameters.
    /// Note: The updatedSince parameter is not currently supported by the endpoint and is ignored.
    /// </summary>
    private static MultipartFormDataContent BuildSongsContent(int page, int records)
    {
        int boundedPage = Math.Max(page, 1);
        int boundedRecords = Math.Clamp(records, 1, 250);

        var content = new MultipartFormDataContent();
        content.Add(new StringContent("release_date"), "sort[0][sort_by]");
        content.Add(new StringContent("ASC"), "sort[0][sort_order]");
        content.Add(new StringContent("full"), "data_type");
        content.Add(new StringContent(boundedPage.ToString(CultureInfo.InvariantCulture)), "page");
        content.Add(new StringContent(boundedRecords.ToString(CultureInfo.InvariantCulture)), "records");

        return content;
    }

    private string ResolveDownloadUrl(JsonElement file)
    {
        string full = ReadString(file, "download_page_url_full");
        if (!string.IsNullOrWhiteSpace(full))
        {
            return full;
        }

        string partial = ReadString(file, "download_page_url");
        if (string.IsNullOrWhiteSpace(partial))
        {
            return string.Empty;
        }

        return new Uri(new Uri(sourceOptions.Value.BaseUrl), partial).ToString();
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out int value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) => value,
            _ => 0,
        };
    }

    private static long ReadLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out long value) => value,
            JsonValueKind.String when long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) => value,
            _ => 0,
        };
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out int value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) => value,
            _ => null,
        };
    }

    private static long? ReadNullableLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out long value) => value,
            JsonValueKind.String when long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) => value,
            _ => null,
        };
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return string.Empty;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? string.Empty;
        }

        return property.ValueKind == JsonValueKind.Null ? string.Empty : property.ToString();
    }

    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        string? value = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
