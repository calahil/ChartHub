using ChartHub.Utilities;

namespace ChartHub.Services.Transfers;

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

    Task<string> GetChartHubFolderIdAsync(CancellationToken cancellationToken = default);
}

internal static class NameConflictResolver
{
    public static string ResolveUniqueName(string originalName, Func<string, bool> exists)
    {
        originalName = SafePathHelper.SanitizeFileName(originalName, "download.bin");

        if (!exists(originalName))
        {
            return originalName;
        }

        string extension = Path.GetExtension(originalName);
        string baseName = Path.GetFileNameWithoutExtension(originalName);

        for (int i = 1; i < 10_000; i++)
        {
            string candidate = string.IsNullOrWhiteSpace(extension)
                ? $"{baseName} ({i})"
                : $"{baseName} ({i}){extension}";

            if (!exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Unable to resolve a unique file name for '{originalName}'.");
    }
}
