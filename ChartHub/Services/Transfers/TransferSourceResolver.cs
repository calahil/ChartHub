using ChartHub.Services;

namespace ChartHub.Services.Transfers;

public sealed class TransferSourceResolver : ITransferSourceResolver
{
    private readonly UrlHelper _urlHelper = new();

    public async Task<ResolvedTransferSource> ResolveAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return new ResolvedTransferSource(sourceUrl, sourceUrl, TransferSourceKind.Unknown);

        var finalUrl = await _urlHelper.GetFinalRedirectUrlAsync(sourceUrl);

        if (finalUrl.StartsWith("https://drive.google.com/drive", StringComparison.OrdinalIgnoreCase))
        {
            var driveId = UrlExtractor.ExtractIdFromUrl(finalUrl);
            return new ResolvedTransferSource(sourceUrl, finalUrl, TransferSourceKind.GoogleDriveFolder, driveId);
        }

        if (finalUrl.StartsWith("https://drive.google.com/file", StringComparison.OrdinalIgnoreCase))
        {
            var driveId = UrlExtractor.ExtractIdFromUrl(finalUrl);
            return new ResolvedTransferSource(sourceUrl, finalUrl, TransferSourceKind.GoogleDriveFile, driveId);
        }

        if (finalUrl.StartsWith("https://www.mediafire.com", StringComparison.OrdinalIgnoreCase)
            || finalUrl.StartsWith("http://www.mediafire.com", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedTransferSource(sourceUrl, finalUrl, TransferSourceKind.MediaFire);
        }

        if (finalUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || finalUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedTransferSource(sourceUrl, finalUrl, TransferSourceKind.DirectHttp);
        }

        return new ResolvedTransferSource(sourceUrl, finalUrl, TransferSourceKind.Unknown);
    }
}
