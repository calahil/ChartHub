namespace ChartHub.Services.Transfers;

public enum TransferSourceKind
{
    Unknown,
    DirectHttp,
    MediaFire,
    GoogleDriveFile,
    GoogleDriveFolder,
}

public sealed record ResolvedTransferSource(
    string OriginalUrl,
    string FinalUrl,
    TransferSourceKind Kind,
    string? DriveId = null);

public interface ITransferSourceResolver
{
    Task<ResolvedTransferSource> ResolveAsync(string sourceUrl, CancellationToken cancellationToken = default);
}
