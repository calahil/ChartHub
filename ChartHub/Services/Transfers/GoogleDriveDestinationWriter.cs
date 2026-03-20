namespace ChartHub.Services.Transfers;

public sealed class GoogleDriveDestinationWriter(IGoogleDriveClient googleDriveClient) : IGoogleDriveDestinationWriter
{
    private readonly IGoogleDriveClient _googleDriveClient = googleDriveClient;

    public async Task<DestinationWriteResult> WriteFromTempAsync(
        string tempFilePath,
        string desiredName,
        CancellationToken cancellationToken = default)
    {
        var folderId = await GetChartHubFolderIdAsync(cancellationToken);
        var finalName = await ResolveUniqueNameAsync(folderId, desiredName, cancellationToken);

        var uploadedFileId = await _googleDriveClient.UploadFileAsync(folderId, tempFilePath, finalName);

        return new DestinationWriteResult(
            FinalName: finalName,
            FinalLocation: uploadedFileId,
            DestinationContainer: folderId);
    }

    public async Task<DestinationWriteResult?> TryCopyDriveFileAsync(
        string sourceFileId,
        string desiredName,
        CancellationToken cancellationToken = default)
    {
        var folderId = await GetChartHubFolderIdAsync(cancellationToken);
        var finalName = await ResolveUniqueNameAsync(folderId, desiredName, cancellationToken);

        try
        {
            var copiedFileId = await _googleDriveClient.CopyFileIntoFolderAsync(sourceFileId, folderId, finalName);

            return new DestinationWriteResult(
                FinalName: finalName,
                FinalLocation: copiedFileId,
                DestinationContainer: folderId);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> GetChartHubFolderIdAsync(CancellationToken cancellationToken = default)
    {
        await _googleDriveClient.InitializeAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(_googleDriveClient.ChartHubFolderId))
            throw new InvalidOperationException("Google Drive folder is not initialised.");

        return _googleDriveClient.ChartHubFolderId;
    }

    private async Task<string> ResolveUniqueNameAsync(
        string folderId,
        string desiredName,
        CancellationToken cancellationToken)
    {
        var existing = await _googleDriveClient.ListFilesAsync(folderId);
        var existingNames = existing
            .Where(f => !string.IsNullOrWhiteSpace(f.Name))
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return NameConflictResolver.ResolveUniqueName(desiredName, name => existingNames.Contains(name));
    }
}
