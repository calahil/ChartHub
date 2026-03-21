using System.Security.Cryptography;
using System.Text;

namespace ChartHub.Services;

public static class LibraryIdentityService
{
    private static readonly string[] ChartExtensions = [".chart", ".mid", ".midi"];
    private static readonly string[] AudioExtensions = [".ogg", ".opus", ".mp3", ".wav", ".flac", ".m4a", ".aac"];

    public static string BuildSourceKey(string source, string? sourceId)
    {
        var normalizedSource = NormalizeSource(source);
        var normalizedSourceId = string.IsNullOrWhiteSpace(sourceId) ? "unknown" : EscapeComponent(sourceId.Trim());
        return $"{normalizedSource}|sourceId={normalizedSourceId}";
    }

    public static string NormalizeSourceKey(string source, string? sourceId)
    {
        var normalizedSource = NormalizeSource(source);
        if (!string.IsNullOrWhiteSpace(sourceId)
            && sourceId.StartsWith(normalizedSource + "|", StringComparison.OrdinalIgnoreCase))
            return sourceId;

        return BuildSourceKey(normalizedSource, sourceId);
    }

    public static string BuildEncoreSourceKey(int chartId, string? md5)
    {
        var parts = new List<string>();
        if (chartId > 0)
            parts.Add($"chartId={chartId}");

        if (!string.IsNullOrWhiteSpace(md5))
            parts.Add($"md5={EscapeComponent(md5.Trim())}");

        if (parts.Count == 0)
            parts.Add("unknown");

        return $"{LibrarySourceNames.Encore}|{string.Join("|", parts)}";
    }

    public static string BuildExternalKeyHash(string sourceKey)
    {
        return ComputeSha256Hex(sourceKey ?? string.Empty);
    }

    public static string BuildInternalIdentityKey(string contentIdentityHash, SongMetadata metadata)
    {
        var artist = NormalizeIdentityComponent(metadata.Artist);
        var title = NormalizeIdentityComponent(metadata.Title);
        var charter = NormalizeIdentityComponent(metadata.Charter);
        return ComputeSha256Hex($"{contentIdentityHash}|artist={artist}|title={title}|charter={charter}");
    }

    public static async Task<string> ComputeInstalledContentIdentityHashAsync(string installDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
            return ComputeSha256Hex(string.Empty);

        var files = Directory
            .EnumerateFiles(installDirectory, "*", SearchOption.AllDirectories)
            .Where(IsIdentityFile)
            .OrderBy(path => Path.GetRelativePath(installDirectory, path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
            return ComputeSha256Hex(string.Empty);

        var builder = new StringBuilder();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(installDirectory, file).Replace('\\', '/').ToLowerInvariant();
            var fileHash = await ComputeFileSha256HexAsync(file, cancellationToken).ConfigureAwait(false);
            builder.Append(relativePath).Append(':').Append(fileHash).Append('\n');
        }

        return ComputeSha256Hex(builder.ToString());
    }

    private static bool IsIdentityFile(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("song.ini", StringComparison.OrdinalIgnoreCase))
            return true;

        var extension = Path.GetExtension(path);
        return ChartExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<string> ComputeFileSha256HexAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeSha256Hex(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeIdentityComponent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        return string.Join(' ', value.Trim().ToLowerInvariant().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeSource(string? source)
    {
        return LibrarySourceNames.NormalizeTrustedSource(source);
    }

    private static string EscapeComponent(string value)
    {
        return Uri.EscapeDataString(value);
    }
}