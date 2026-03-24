using ChartHub.Services;

namespace ChartHub.Services.Transfers;

public sealed class TransferSourceResolver : ITransferSourceResolver
{
    private readonly UrlHelper _urlHelper = new();

    public async Task<ResolvedTransferSource> ResolveAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return new ResolvedTransferSource(sourceUrl, sourceUrl, TransferSourceKind.Unknown);
        }

        string finalUrl = await _urlHelper.GetFinalRedirectUrlAsync(sourceUrl);
        if (TryResolveGoogleDriveSource(sourceUrl, finalUrl, out ResolvedTransferSource googleDriveSource))
        {
            return googleDriveSource;
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

    internal static bool TryResolveGoogleDriveSource(
        string sourceUrl,
        string finalUrl,
        out ResolvedTransferSource resolved)
    {
        resolved = default!;
        bool sourceResolved = TryResolveGoogleDriveFromUrl(sourceUrl, out TransferSourceKind sourceKind, out string? sourceDriveId);
        bool finalResolved = TryResolveGoogleDriveFromUrl(finalUrl, out TransferSourceKind finalKind, out string? finalDriveId);

        if (!sourceResolved && !finalResolved)
        {
            return false;
        }

        if (sourceResolved && sourceKind == TransferSourceKind.GoogleDriveFile && !string.IsNullOrWhiteSpace(sourceDriveId))
        {
            resolved = new ResolvedTransferSource(sourceUrl, finalUrl, TransferSourceKind.GoogleDriveFile, sourceDriveId);
            return true;
        }

        if (finalResolved && !string.IsNullOrWhiteSpace(finalDriveId))
        {
            resolved = new ResolvedTransferSource(sourceUrl, finalUrl, finalKind, finalDriveId);
            return true;
        }

        if (sourceResolved && !string.IsNullOrWhiteSpace(sourceDriveId))
        {
            resolved = new ResolvedTransferSource(sourceUrl, finalUrl, sourceKind, sourceDriveId);
            return true;
        }

        return false;
    }

    private static bool TryResolveGoogleDriveFromUrl(
        string? url,
        out TransferSourceKind kind,
        out string? driveId)
    {
        kind = TransferSourceKind.Unknown;
        driveId = null;

        if (string.IsNullOrWhiteSpace(url)
            || !url.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (url.Contains("/file/", StringComparison.OrdinalIgnoreCase))
        {
            kind = TransferSourceKind.GoogleDriveFile;
        }
        else if (url.Contains("/folders/", StringComparison.OrdinalIgnoreCase)
            || url.Contains("/drive/folders/", StringComparison.OrdinalIgnoreCase))
        {
            kind = TransferSourceKind.GoogleDriveFolder;
        }
        else
        {
            return false;
        }

        try
        {
            driveId = UrlExtractor.ExtractIdFromUrl(url);
            return !string.IsNullOrWhiteSpace(driveId);
        }
        catch
        {
            return false;
        }
    }
}
