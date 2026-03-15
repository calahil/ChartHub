using RhythmVerseClient.Models;

namespace RhythmVerseClient.Services.Transfers;

public enum TransferDestinationKind
{
    LocalStorage,
    GoogleDrive,
}

public enum TransferStage
{
    Queued,
    ResolvingSource,
    Cancelling,
    CopyingInGoogleDrive,
    DownloadingFolder,
    ZippingFolder,
    Downloading,
    MovingToDestination,
    Uploading,
    Cancelled,
    Completed,
    Failed,
}

public sealed record TransferRequest(
    string DisplayName,
    string SourceUrl,
    long? SourceFileSize,
    TransferDestinationKind Destination);

public sealed record TransferResult(
    bool Success,
    TransferStage FinalStage,
    string? FinalLocation,
    string? Error,
    DownloadFile DownloadItem);

public sealed record TransferProgressUpdate(
    TransferStage Stage,
    double? ProgressPercent = null);
