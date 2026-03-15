namespace RhythmVerseClient.Services.Transfers;

public sealed record DestinationWriteResult(
    string FinalName,
    string FinalLocation,
    string DestinationContainer);

public interface ILocalDestinationWriter
{
    Task<DestinationWriteResult> WriteFromTempAsync(
        string tempFilePath,
        string desiredName,
        CancellationToken cancellationToken = default);

    string ResolveUniqueName(string desiredName);
}

public interface IGoogleDriveDestinationWriter
{
    Task<DestinationWriteResult> WriteFromTempAsync(
        string tempFilePath,
        string desiredName,
        CancellationToken cancellationToken = default);

    Task<DestinationWriteResult?> TryCopyDriveFileAsync(
        string sourceFileId,
        string desiredName,
        CancellationToken cancellationToken = default);

    Task<string> GetRhythmVerseFolderIdAsync(CancellationToken cancellationToken = default);
}

internal static class NameConflictResolver
{
    public static string ResolveUniqueName(string originalName, Func<string, bool> exists)
    {
        if (!exists(originalName))
            return originalName;

        var extension = Path.GetExtension(originalName);
        var baseName = Path.GetFileNameWithoutExtension(originalName);

        for (var i = 1; i < 10_000; i++)
        {
            var candidate = string.IsNullOrWhiteSpace(extension)
                ? $"{baseName} ({i})"
                : $"{baseName} ({i}){extension}";

            if (!exists(candidate))
                return candidate;
        }

        throw new IOException($"Unable to resolve a unique file name for '{originalName}'.");
    }
}
