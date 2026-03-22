using ChartHub.Utilities;

namespace ChartHub.Services.Transfers;

public sealed class LocalDestinationWriter(AppGlobalSettings settings) : ILocalDestinationWriter
{
    private readonly AppGlobalSettings _settings = settings;

    public Task<DestinationWriteResult> WriteFromTempAsync(
        string tempFilePath,
        string desiredName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string finalName = ResolveUniqueName(desiredName);
        string finalPath = Path.Combine(_settings.DownloadDir, finalName);
        File.Move(tempFilePath, finalPath, overwrite: false);

        return Task.FromResult(new DestinationWriteResult(
            FinalName: finalName,
            FinalLocation: finalPath,
            DestinationContainer: _settings.DownloadDir));
    }

    public string ResolveUniqueName(string desiredName)
    {
        return NameConflictResolver.ResolveUniqueName(
            desiredName,
            name => File.Exists(Path.Combine(_settings.DownloadDir, name)));
    }
}
