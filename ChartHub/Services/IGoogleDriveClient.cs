using System.Collections.ObjectModel;
using System.Diagnostics;

using ChartHub.Models;
using ChartHub.Services.Transfers;
using ChartHub.Utilities;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;

namespace ChartHub.Services;

public interface IGoogleDriveClient
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<bool> TryInitializeSilentAsync(CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);

    Task DownloadFolderAsZipAsync(
        string folderId,
        string zipFilePath,
        IProgress<TransferProgressUpdate>? stageProgress = null,
        CancellationToken cancellationToken = default);
    Task DownloadFileAsync(string fileId, string saveToPath);
}

public class GoogleDriveClient : IGoogleDriveClient, IAsyncDisposable
{
    private DriveService? _driveService;
    private readonly IGoogleAuthProvider _authProvider;
    private readonly SemaphoreSlim _serviceInitLock = new(1, 1);
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private static readonly string[] Scopes = [DriveService.Scope.DriveReadonly, DriveService.Scope.DriveFile];
    private static readonly string ApplicationName = "ChartHub";
    public string ChartHubFolderId { get; private set; } = string.Empty;
    private UserCredential? _credential;

    public GoogleDriveClient(IGoogleAuthProvider authProvider)
    {
        _authProvider = authProvider;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await InitializeCoreAsync(allowInteractiveFallback: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryInitializeSilentAsync(CancellationToken cancellationToken = default)
    {
        return await InitializeCoreAsync(allowInteractiveFallback: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInfo("Drive", "Google Drive sign-out started", new Dictionary<string, object?>
        {
            ["folderId"] = ChartHubFolderId,
        });

        await _serviceInitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopMonitoringAsync().ConfigureAwait(false);
            _driveService?.Dispose();
            _driveService = null;
            await _authProvider.SignOutAsync(_credential, cancellationToken).ConfigureAwait(false);
            _credential = null;
            ChartHubFolderId = string.Empty;
        }
        finally
        {
            _serviceInitLock.Release();
        }

        Logger.LogInfo("Drive", "Google Drive sign-out completed");
    }

    public async Task<DriveService> GetServiceAsync(CancellationToken cancellationToken = default)
    {
        if (_driveService is not null)
        {
            return _driveService;
        }

        await _serviceInitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_driveService is not null)
            {
                return _driveService;
            }

            if (_credential is null)
            {
                Logger.LogInfo("Auth", "Attempting silent Google authorization");
                _credential = await _authProvider.TryAuthorizeSilentAsync(Scopes, cancellationToken).ConfigureAwait(false)
                    ?? await _authProvider.AuthorizeInteractiveAsync(Scopes, cancellationToken).ConfigureAwait(false);
                Logger.LogInfo("Auth", "Google authorization credential acquired", new Dictionary<string, object?>
                {
                    ["method"] = _credential is null ? "none" : "silent-or-interactive",
                });
            }

            _driveService = CreateDriveService(_credential!);

            Logger.LogInfo("Drive", "Google Drive service client created", new Dictionary<string, object?>
            {
                ["applicationName"] = ApplicationName,
            });

            return _driveService;
        }
        finally
        {
            _serviceInitLock.Release();
        }
    }

    private async Task<bool> InitializeCoreAsync(bool allowInteractiveFallback, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.LogInfo("Drive", "Google Drive initialization started", new Dictionary<string, object?>
        {
            ["allowInteractiveFallback"] = allowInteractiveFallback,
        });

        await _serviceInitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopMonitoringAsync().ConfigureAwait(false);
            _driveService?.Dispose();
            _driveService = null;

            if (_credential is null)
            {
                Logger.LogInfo("Auth", "Attempting silent Google authorization");
                _credential = await _authProvider.TryAuthorizeSilentAsync(Scopes, cancellationToken).ConfigureAwait(false);

                if (_credential is null)
                {
                    if (!allowInteractiveFallback)
                    {
                        Logger.LogInfo("Auth", "Silent Google authorization unavailable");
                        return false;
                    }

                    _credential = await _authProvider.AuthorizeInteractiveAsync(Scopes, cancellationToken).ConfigureAwait(false);
                }
            }

            _driveService = CreateDriveService(_credential!);

            Logger.LogInfo("Drive", "Google Drive service client created", new Dictionary<string, object?>
            {
                ["applicationName"] = ApplicationName,
            });

            ChartHubFolderId = await CreateDirectoryAsync("ChartHub").ConfigureAwait(false);
            _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _monitorTask = MonitorDirectoryAsync(ChartHubFolderId, TimeSpan.FromSeconds(30), OnFileChanged, _monitorCts.Token);

            Logger.LogInfo("Drive", "Google Drive initialization completed", new Dictionary<string, object?>
            {
                ["folderId"] = ChartHubFolderId,
                ["elapsedMs"] = stopwatch.ElapsedMilliseconds,
                ["allowInteractiveFallback"] = allowInteractiveFallback,
            });

            return true;
        }
        catch (Exception ex)
        {
            _driveService?.Dispose();
            _driveService = null;
            ChartHubFolderId = string.Empty;

            if (allowInteractiveFallback)
            {
                Logger.LogError("Drive", "Google Drive initialization failed", ex, new Dictionary<string, object?>
                {
                    ["elapsedMs"] = stopwatch.ElapsedMilliseconds,
                });
                throw;
            }

            Logger.LogWarning("Drive", "Silent Google Drive initialization failed", new Dictionary<string, object?>
            {
                ["elapsedMs"] = stopwatch.ElapsedMilliseconds,
                ["error"] = ex.Message,
            });
            return false;
        }
        finally
        {
            _serviceInitLock.Release();
        }
    }

    private static DriveService CreateDriveService(UserCredential credential)
    {
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName,
        });
    }

    private void OnFileChanged(Google.Apis.Drive.v3.Data.File file, string changeType)
    {
        if (changeType == "created")
        {
            ChartHub.Utilities.Logger.LogInfo("Drive", "Remote file created", new Dictionary<string, object?>
            {
                ["changeType"] = changeType,
                ["driveFileId"] = file.Id,
                ["fileName"] = file.Name,
            });
        }
        else if (changeType == "deleted")
        {
            ChartHub.Utilities.Logger.LogInfo("Drive", "Remote file deleted", new Dictionary<string, object?>
            {
                ["changeType"] = changeType,
                ["driveFileId"] = file.Id,
                ["fileName"] = file.Name,
            });
        }
    }

    public async Task MonitorDirectoryAsync(string directoryId, TimeSpan pollingInterval, Action<Google.Apis.Drive.v3.Data.File, string> onFileChanged, CancellationToken cancellationToken = default)
    {
        var existingFiles = new Dictionary<string, Google.Apis.Drive.v3.Data.File>();

        while (!cancellationToken.IsCancellationRequested)
        {
            IList<Google.Apis.Drive.v3.Data.File> files = await ListFilesAsync(directoryId).ConfigureAwait(false);

            foreach (Google.Apis.Drive.v3.Data.File file in files)
            {
                if (!existingFiles.ContainsKey(file.Id))
                {
                    existingFiles[file.Id] = file;
                    onFileChanged?.Invoke(file, "created");
                }
            }

            foreach (string? existingFileId in existingFiles.Keys.ToList())
            {
                if (!files.Any(f => f.Id == existingFileId))
                {
                    Google.Apis.Drive.v3.Data.File deletedFile = existingFiles[existingFileId];
                    existingFiles.Remove(existingFileId);
                    onFileChanged?.Invoke(deletedFile, "deleted");
                }
            }

            await Task.Delay(pollingInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StopMonitoringAsync()
    {
        CancellationTokenSource? monitorCts = _monitorCts;
        Task? monitorTask = _monitorTask;

        _monitorCts = null;
        _monitorTask = null;

        if (monitorCts is null)
        {
            return;
        }

        monitorCts.Cancel();
        try
        {
            if (monitorTask is not null)
            {
                await monitorTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogInfo("Drive", "Google Drive monitoring cancelled", new Dictionary<string, object?>
            {
                ["folderId"] = ChartHubFolderId,
            });
        }
        finally
        {
            monitorCts.Dispose();
        }
    }

    public async Task<string> CreateDirectoryAsync(string directoryName)
    {
        string folderId = await GetDirectoryIdAsync(directoryName);
        if (!string.IsNullOrEmpty(folderId))
        {
            return folderId;
        }

        var fileMetadata = new Google.Apis.Drive.v3.Data.File()
        {
            Name = directoryName,
            MimeType = "application/vnd.google-apps.folder"
        };
        FilesResource.CreateRequest request = _driveService!.Files.Create(fileMetadata);
        request.Fields = "id";
        Google.Apis.Drive.v3.Data.File file = await request.ExecuteAsync();
        return file.Id ?? string.Empty;
    }

    public async Task<string> GetDirectoryIdAsync(string directoryName)
    {
        FilesResource.ListRequest request = _driveService!.Files.List();
        request.Q = $"mimeType='application/vnd.google-apps.folder' and name='{directoryName.Replace("'", "\\'")}' and trashed=false";
        request.Spaces = "drive";
        request.Fields = "files(id, name)";
        request.PageSize = 1;

        Google.Apis.Drive.v3.Data.FileList result = await request.ExecuteAsync();
        Google.Apis.Drive.v3.Data.File? folder = result.Files.FirstOrDefault();

        return folder?.Id ?? string.Empty;
    }

    public async Task<string> UploadFileAsync(string directoryId, string filePath, string? desiredFileName = null)
    {
        var fileMetadata = new Google.Apis.Drive.v3.Data.File()
        {
            Name = string.IsNullOrWhiteSpace(desiredFileName) ? Path.GetFileName(filePath) : desiredFileName,
            Parents = new List<string> { directoryId }
        };

        using var stream = new FileStream(filePath, FileMode.Open);
        FilesResource.CreateMediaUpload request = _driveService!.Files.Create(fileMetadata, stream, GetMimeType(filePath));
        request.Fields = "id";
        await request.UploadAsync();
        Google.Apis.Drive.v3.Data.File file = request.ResponseBody;
        return file?.Id ?? string.Empty;
    }

    public async Task<string> CopyFileIntoFolderAsync(string sourceFileId, string destinationFolderId, string desiredFileName)
    {
        _driveService ??= await GetServiceAsync();

        var metadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = desiredFileName,
            Parents = new List<string> { destinationFolderId },
        };

        FilesResource.CopyRequest request = _driveService.Files.Copy(metadata, sourceFileId);
        request.Fields = "id,name";
        Google.Apis.Drive.v3.Data.File copied = await request.ExecuteAsync();
        return copied.Id;
    }

    public async Task DownloadFileAsync(string fileId, string saveToPath)
    {
        FilesResource.GetRequest request = _driveService!.Files.Get(fileId);
        using var stream = new MemoryStream();
        await request.DownloadAsync(stream);
        stream.Position = 0;
        using var fileStream = new FileStream(saveToPath, FileMode.Create, FileAccess.Write);
        stream.WriteTo(fileStream);
    }

    public async Task DownloadFolderAsZipAsync(
        string folderId,
        string zipFilePath,
        IProgress<TransferProgressUpdate>? stageProgress = null,
        CancellationToken cancellationToken = default)
    {
        _driveService ??= await GetServiceAsync(cancellationToken);

        string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string folderName = await GetDriveItemNameAsync(folderId, cancellationToken);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                folderName = folderId;
            }

            string stagedRoot = GoogleDriveFolderDownloadHelper.GetUniqueDirectoryPath(tempRoot, folderName);
            Directory.CreateDirectory(stagedRoot);

            stageProgress?.Report(new TransferProgressUpdate(TransferStage.DownloadingFolder, 35));
            await DownloadFilesInFolderRecursiveAsync(folderId, stagedRoot, cancellationToken);

            stageProgress?.Report(new TransferProgressUpdate(TransferStage.ZippingFolder, 70));
            CreateZip(tempRoot, zipFilePath);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private async Task DownloadFilesInFolderRecursiveAsync(string folderId, string destinationFolder, CancellationToken cancellationToken)
    {
        _driveService ??= await GetServiceAsync(cancellationToken);

        FilesResource.ListRequest request = _driveService.Files.List();
        request.Q = $"'{folderId}' in parents and trashed=false";
        request.Fields = "files(id, name, mimeType)";

        Google.Apis.Drive.v3.Data.FileList result = await request.ExecuteAsync(cancellationToken);
        foreach (Google.Apis.Drive.v3.Data.File? file in result.Files)
        {
            if (file.MimeType == "application/vnd.google-apps.folder")
            {
                string childPath = GoogleDriveFolderDownloadHelper.GetUniqueDirectoryPath(destinationFolder, file.Name);
                Directory.CreateDirectory(childPath);
                await DownloadFilesInFolderRecursiveAsync(file.Id, childPath, cancellationToken);
                continue;
            }

            await DownloadDriveFileAsync(file, destinationFolder, cancellationToken);
        }
    }

    private async Task DownloadDriveFileAsync(
        Google.Apis.Drive.v3.Data.File file,
        string destinationFolder,
        CancellationToken cancellationToken)
    {
        if (GoogleDriveFolderDownloadHelper.TryGetExportDescriptor(file.MimeType, out GoogleDriveExportDescriptor exportDescriptor))
        {
            string exportPath = GoogleDriveFolderDownloadHelper.GetUniqueFilePath(destinationFolder, file.Name, exportDescriptor.FileExtension);
            using var exportStream = new FileStream(exportPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await _driveService!.Files.Export(file.Id, exportDescriptor.ExportMimeType)
                .DownloadAsync(exportStream, cancellationToken);
            return;
        }

        if (GoogleDriveFolderDownloadHelper.IsGoogleWorkspaceMimeType(file.MimeType))
        {
            Logger.LogWarning("Drive", "Skipping unsupported Google Workspace file during folder download", new Dictionary<string, object?>
            {
                ["driveFileId"] = file.Id,
                ["fileName"] = file.Name,
                ["mimeType"] = file.MimeType,
            });
            return;
        }

        string filePath = GoogleDriveFolderDownloadHelper.GetUniqueFilePath(destinationFolder, file.Name);
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await _driveService!.Files.Get(file.Id).DownloadAsync(fileStream, cancellationToken);
    }

    private async Task<string> GetDriveItemNameAsync(string itemId, CancellationToken cancellationToken)
    {
        _driveService ??= await GetServiceAsync(cancellationToken);
        FilesResource.GetRequest request = _driveService.Files.Get(itemId);
        request.Fields = "name";
        Google.Apis.Drive.v3.Data.File item = await request.ExecuteAsync(cancellationToken);
        return item.Name ?? string.Empty;
    }

    private static void CreateZip(string sourceFolderPath, string destinationZipFilePath)
    {
        using var archive = ZipArchive.Create();
        AddFilesToArchive(archive, sourceFolderPath, sourceFolderPath);

        using FileStream stream = File.Open(destinationZipFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        archive.SaveTo(stream, CompressionType.Deflate);
    }

    private static void AddFilesToArchive(ZipArchive archive, string rootPath, string currentPath)
    {
        foreach (string filePath in Directory.GetFiles(currentPath))
        {
            string relativePath = Path.GetRelativePath(rootPath, filePath);
            archive.AddEntry(relativePath, filePath);
        }

        foreach (string subDirPath in Directory.GetDirectories(currentPath))
        {
            AddFilesToArchive(archive, rootPath, subDirPath);
        }
    }

    public async Task DeleteFileAsync(string fileId)
    {
        FilesResource.DeleteRequest request = _driveService!.Files.Delete(fileId);
        await request.ExecuteAsync();
    }

    public async Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesAsync(string directoryId)
    {
        FilesResource.ListRequest request = _driveService!.Files.List();
        request.Q = $"'{directoryId}' in parents";
        request.Fields = "files(id, name, size, mimeType)";
        Google.Apis.Drive.v3.Data.FileList result = await request.ExecuteAsync();
        return result.Files;
    }

    public async Task<ObservableCollection<WatcherFile>> GetFileDataCollectionAsync(string directoryId)
    {
        IList<Google.Apis.Drive.v3.Data.File> files = await ListFilesAsync(directoryId);
        var fileDataCollection = new ObservableCollection<WatcherFile>();

        foreach (Google.Apis.Drive.v3.Data.File file in files)
        {
            WatcherFile fileData = await ConvertToFileDataAsync(file);
            fileDataCollection.Add(fileData);
        }

        return fileDataCollection;
    }

    private async Task<WatcherFile> ConvertToFileDataAsync(Google.Apis.Drive.v3.Data.File file)
    {
        WatcherFileType fileType = DetermineFileType(file.Name);
        string imageFile = GetIconForFileType(fileType);

        long sizeBytes = await GetFileSizeAsync(file.Id);

        return new WatcherFile(
            displayName: file.Name,
            filePath: file.Id,
            watcherFileType: fileType,
            imageFile: imageFile,
            sizeBytes: sizeBytes);
    }

    private WatcherFileType DetermineFileType(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension switch
        {
            ".zip" => WatcherFileType.Zip,
            ".rar" => WatcherFileType.Rar,
            ".7z" => WatcherFileType.SevenZip,
            "" => WatcherFileType.Con,
            _ => WatcherFileType.Unknown,
        };
    }

    private string GetIconForFileType(WatcherFileType fileType)
    {
        string iconFileName = fileType switch
        {
            WatcherFileType.Rar => "rar.png",
            WatcherFileType.Zip => "zip.png",
            WatcherFileType.Con => "rb.png",
            WatcherFileType.SevenZip => "sevenzip.png",
            WatcherFileType.CloneHero => "clonehero.png",
            _ => "blank.png",
        };

        return $"avares://ChartHub/Resources/Images/{iconFileName}";
    }

    private async Task<long> GetFileSizeAsync(string fileId)
    {
        FilesResource.GetRequest request = _driveService!.Files.Get(fileId);
        request.Fields = "size";
        Google.Apis.Drive.v3.Data.File file = await request.ExecuteAsync();
        return file.Size ?? 0;
    }

    private string GetMimeType(string fileName)
    {
        string mimeType = "application/unknown";
        string ext = Path.GetExtension(fileName).ToLower();

        mimeType = ext switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".7z" => "application/x-7z-compressed",
            _ => "application/unknown"
        };
        return mimeType;
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceInitLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopMonitoringAsync().ConfigureAwait(false);
            _driveService?.Dispose();
            _driveService = null;
            _credential = null;
            ChartHubFolderId = string.Empty;
        }
        finally
        {
            _serviceInitLock.Release();
            _serviceInitLock.Dispose();
        }
    }
}

internal readonly record struct GoogleDriveExportDescriptor(string ExportMimeType, string FileExtension);

internal static class GoogleDriveFolderDownloadHelper
{
    private static readonly Dictionary<string, GoogleDriveExportDescriptor> GoogleWorkspaceExportDescriptors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/vnd.google-apps.document"] = new("application/pdf", ".pdf"),
        ["application/vnd.google-apps.spreadsheet"] = new("application/pdf", ".pdf"),
        ["application/vnd.google-apps.presentation"] = new("application/pdf", ".pdf"),
        ["application/vnd.google-apps.drawing"] = new("image/png", ".png"),
    };

    public static bool IsGoogleWorkspaceMimeType(string? mimeType)
    {
        return !string.IsNullOrWhiteSpace(mimeType)
            && mimeType.StartsWith("application/vnd.google-apps.", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mimeType, "application/vnd.google-apps.folder", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetExportDescriptor(string? mimeType, out GoogleDriveExportDescriptor descriptor)
    {
        if (!string.IsNullOrWhiteSpace(mimeType)
            && GoogleWorkspaceExportDescriptors.TryGetValue(mimeType, out descriptor))
        {
            return true;
        }

        descriptor = default;
        return false;
    }

    public static string GetUniqueDirectoryPath(string parentDirectory, string? directoryName)
    {
        string safeName = SafePathHelper.SanitizePathSegment(directoryName, "untitled");
        return GetUniqueChildPath(parentDirectory, safeName);
    }

    public static string GetUniqueFilePath(string parentDirectory, string? fileName, string? requiredExtension = null)
    {
        string safeName = SafePathHelper.SanitizeFileName(fileName, "untitled");
        if (!string.IsNullOrWhiteSpace(requiredExtension)
            && !safeName.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
        {
            safeName += requiredExtension;
        }

        return GetUniqueChildPath(parentDirectory, safeName);
    }

    private static string GetUniqueChildPath(string parentDirectory, string safeName)
    {
        string candidatePath = Path.Combine(parentDirectory, safeName);
        if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
        {
            return candidatePath;
        }

        string baseName = Path.GetFileNameWithoutExtension(safeName);
        string extension = Path.GetExtension(safeName);
        int suffix = 2;

        do
        {
            candidatePath = Path.Combine(parentDirectory, $"{baseName} ({suffix}){extension}");
            suffix++;
        }
        while (File.Exists(candidatePath) || Directory.Exists(candidatePath));

        return candidatePath;
    }
}