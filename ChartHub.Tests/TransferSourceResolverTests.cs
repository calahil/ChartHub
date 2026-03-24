using ChartHub.Services.Transfers;

namespace ChartHub.Tests;

[Trait(TestInfrastructure.TestCategories.Category, TestInfrastructure.TestCategories.Unit)]
public sealed class TransferSourceResolverTests
{
    [Fact]
    public void TryResolveGoogleDriveSource_FileSourceWithFolderRedirect_PrefersFileResolution()
    {
        bool resolved = TransferSourceResolver.TryResolveGoogleDriveSource(
            "https://drive.google.com/file/d/1CdE-swFy12C2AOsHvVelaznBnuJXoJw-/view",
            "https://drive.google.com/drive/folders/abc123",
            out ResolvedTransferSource? result);

        Assert.True(resolved);
        Assert.NotNull(result);
        Assert.Equal(TransferSourceKind.GoogleDriveFile, result.Kind);
        Assert.Equal("1CdE-swFy12C2AOsHvVelaznBnuJXoJw-", result.DriveId);
    }

    [Fact]
    public void TryResolveGoogleDriveSource_FolderSource_ResolvesFolder()
    {
        bool resolved = TransferSourceResolver.TryResolveGoogleDriveSource(
            "https://drive.google.com/drive/folders/abc123",
            "https://drive.google.com/drive/folders/abc123",
            out ResolvedTransferSource? result);

        Assert.True(resolved);
        Assert.NotNull(result);
        Assert.Equal(TransferSourceKind.GoogleDriveFolder, result.Kind);
        Assert.Equal("abc123", result.DriveId);
    }

    [Fact]
    public void TryResolveGoogleDriveSource_NonGoogleUrl_ReturnsFalse()
    {
        bool resolved = TransferSourceResolver.TryResolveGoogleDriveSource(
            "https://example.com/file.zip",
            "https://example.com/file.zip",
            out ResolvedTransferSource? result);

        Assert.False(resolved);
        Assert.Null(result);
    }
}
