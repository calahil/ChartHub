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
        Uri requestUri = BuildSongsUri(page, records, updatedSince);
        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);

        if (!string.IsNullOrWhiteSpace(sourceOptions.Value.Token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sourceOptions.Value.Token);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
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
            JsonElement data = songElement.GetProperty("data");
            JsonElement file = songElement.GetProperty("file");

            songs.Add(new SyncedSong
            {
                SongId = ReadLong(data, "song_id"),
                RecordId = ReadString(data, "record_id"),
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

    private Uri BuildSongsUri(int page, int records, long? updatedSince)
    {
        string baseUrl = sourceOptions.Value.BaseUrl.TrimEnd('/');
        string path = sourceOptions.Value.SongsPath.TrimStart('/');
        int boundedPage = Math.Max(page, 1);
        int boundedRecords = Math.Max(records, 1);
        // The sort and updated_after parameters are advisory hints to the upstream API.
        // Parameter names are inferred from common RhythmVerse API conventions; adjust if needed.
        string uri = updatedSince.HasValue
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{baseUrl}/{path}?start={(boundedPage - 1) * boundedRecords}&records={boundedRecords}&page={boundedPage}&sort=record_updated&sort_dir=desc&updated_after={updatedSince.Value}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{baseUrl}/{path}?start={(boundedPage - 1) * boundedRecords}&records={boundedRecords}&page={boundedPage}");
        return new Uri(uri);
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
}
