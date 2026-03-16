using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using RhythmVerseClient.Models;
using RhythmVerseClient.Services.Transfers;
using RhythmVerseClient.Utilities;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace RhythmVerseClient.Services
{
    public interface IGoogleDriveClient
    {
        Task<string> CreateDirectoryAsync(string directoryName);
        Task<string> GetDirectoryIdAsync(string directoryName);
        Task<string> UploadFileAsync(string directoryId, string filePath, string? desiredFileName = null);
        Task<string> CopyFileIntoFolderAsync(string sourceFileId, string destinationFolderId, string desiredFileName);
        Task DownloadFolderAsZipAsync(
            string folderId,
            string zipFilePath,
            IProgress<TransferProgressUpdate>? stageProgress = null,
            CancellationToken cancellationToken = default);
        Task DownloadFileAsync(string fileId, string saveToPath);
        Task DeleteFileAsync(string fileId);
        Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesAsync(string directoryId);
        Task MonitorDirectoryAsync(string directoryId, TimeSpan pollingInterval, Action<Google.Apis.Drive.v3.Data.File, string> onFileChanged, CancellationToken cancellationToken = default);
        public string RhythmVerseFolderId { get; }

        Task InitializeAsync(CancellationToken cancellationToken = default);
        Task SignOutAsync(CancellationToken cancellationToken = default);
        Task<ObservableCollection<WatcherFile>> GetFileDataCollectionAsync(string directoryId);
    }

    public class GoogleDriveClient : IGoogleDriveClient, IAsyncDisposable
    {
        private DriveService? _driveService;
        private readonly IGoogleAuthProvider _authProvider;
        private CancellationTokenSource? _monitorCts;
        private Task? _monitorTask;
        private static readonly string[] Scopes = [DriveService.Scope.DriveReadonly, DriveService.Scope.DriveFile];
        private static readonly string ApplicationName = "RhythmVerseClient";
        public string RhythmVerseFolderId { get; private set; } = string.Empty;
        private UserCredential? _credential;

        public GoogleDriveClient(IGoogleAuthProvider authProvider)
        {
            _authProvider = authProvider;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            Logger.LogInfo("Drive", "Google Drive initialization started");

            await StopMonitoringAsync().ConfigureAwait(false);
            try
            {
                _driveService = await GetServiceAsync(cancellationToken);
                RhythmVerseFolderId = await CreateDirectoryAsync("RhythmVerse");
                _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _monitorTask = MonitorDirectoryAsync(RhythmVerseFolderId, TimeSpan.FromSeconds(30), OnFileChanged, _monitorCts.Token);

                Logger.LogInfo("Drive", "Google Drive initialization completed", new Dictionary<string, object?>
                {
                    ["folderId"] = RhythmVerseFolderId,
                    ["elapsedMs"] = stopwatch.ElapsedMilliseconds,
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("Drive", "Google Drive initialization failed", ex, new Dictionary<string, object?>
                {
                    ["elapsedMs"] = stopwatch.ElapsedMilliseconds,
                });
                throw;
            }
        }

        public async Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            Logger.LogInfo("Drive", "Google Drive sign-out started", new Dictionary<string, object?>
            {
                ["folderId"] = RhythmVerseFolderId,
            });

            await StopMonitoringAsync().ConfigureAwait(false);
            _driveService?.Dispose();
            _driveService = null;
            await _authProvider.SignOutAsync(_credential, cancellationToken);
            _credential = null;
            RhythmVerseFolderId = string.Empty;

            Logger.LogInfo("Drive", "Google Drive sign-out completed");
        }

        public async Task<DriveService> GetServiceAsync(CancellationToken cancellationToken = default)
        {
            if (_driveService is not null)
                return _driveService;

            if (_credential is null)
            {
                Logger.LogInfo("Auth", "Attempting silent Google authorization");
                _credential = await _authProvider.TryAuthorizeSilentAsync(Scopes, cancellationToken)
                    ?? await _authProvider.AuthorizeInteractiveAsync(Scopes, cancellationToken);
                Logger.LogInfo("Auth", "Google authorization credential acquired", new Dictionary<string, object?>
                {
                    ["method"] = _credential is null ? "none" : "silent-or-interactive",
                });
            }

            _driveService = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = _credential,
                ApplicationName = ApplicationName,
            });

            Logger.LogInfo("Drive", "Google Drive service client created", new Dictionary<string, object?>
            {
                ["applicationName"] = ApplicationName,
            });

            return _driveService;
        }

        private void OnFileChanged(Google.Apis.Drive.v3.Data.File file, string changeType)
        {
            if (changeType == "created")
            {
                RhythmVerseClient.Utilities.Logger.LogInfo("Drive", "Remote file created", new Dictionary<string, object?>
                {
                    ["changeType"] = changeType,
                    ["driveFileId"] = file.Id,
                    ["fileName"] = file.Name,
                });
            }
            else if (changeType == "deleted")
            {
                RhythmVerseClient.Utilities.Logger.LogInfo("Drive", "Remote file deleted", new Dictionary<string, object?>
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
                var files = await ListFilesAsync(directoryId).ConfigureAwait(false);

                foreach (var file in files)
                {
                    if (!existingFiles.ContainsKey(file.Id))
                    {
                        existingFiles[file.Id] = file;
                        onFileChanged?.Invoke(file, "created");
                    }
                }

                foreach (var existingFileId in existingFiles.Keys.ToList())
                {
                    if (!files.Any(f => f.Id == existingFileId))
                    {
                        var deletedFile = existingFiles[existingFileId];
                        existingFiles.Remove(existingFileId);
                        onFileChanged?.Invoke(deletedFile, "deleted");
                    }
                }

                await Task.Delay(pollingInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task StopMonitoringAsync()
        {
            var monitorCts = _monitorCts;
            var monitorTask = _monitorTask;

            _monitorCts = null;
            _monitorTask = null;

            if (monitorCts is null)
                return;

            monitorCts.Cancel();
            try
            {
                if (monitorTask is not null)
                    await monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Logger.LogInfo("Drive", "Google Drive monitoring cancelled", new Dictionary<string, object?>
                {
                    ["folderId"] = RhythmVerseFolderId,
                });
            }
            finally
            {
                monitorCts.Dispose();
            }
        }

        public async Task<string> CreateDirectoryAsync(string directoryName)
        {
            var folderId = await GetDirectoryIdAsync(directoryName);
            if (!string.IsNullOrEmpty(folderId))
            {
                return folderId;
            }

            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = directoryName,
                MimeType = "application/vnd.google-apps.folder"
            };
            var request = _driveService!.Files.Create(fileMetadata);
            request.Fields = "id";
            var file = await request.ExecuteAsync();
            return file.Id ?? string.Empty;
        }

        public async Task<string> GetDirectoryIdAsync(string directoryName)
        {
            var request = _driveService!.Files.List();
            request.Q = $"mimeType='application/vnd.google-apps.folder' and name='{directoryName.Replace("'", "\\'")}' and trashed=false";
            request.Spaces = "drive";
            request.Fields = "files(id, name)";
            request.PageSize = 1;

            var result = await request.ExecuteAsync();
            var folder = result.Files.FirstOrDefault();

            return folder?.Id ?? string.Empty;
        }

        public async Task<string> UploadFileAsync(string directoryId, string filePath, string? desiredFileName = null)
        {
            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = string.IsNullOrWhiteSpace(desiredFileName) ? Path.GetFileName(filePath) : desiredFileName,
                Parents = new List<string> { directoryId }
            };

            using (var stream = new FileStream(filePath, FileMode.Open))
            {
                var request = _driveService!.Files.Create(fileMetadata, stream, GetMimeType(filePath));
                request.Fields = "id";
                await request.UploadAsync();
                var file = request.ResponseBody;
                return file?.Id ?? string.Empty;
            }
        }

        public async Task<string> CopyFileIntoFolderAsync(string sourceFileId, string destinationFolderId, string desiredFileName)
        {
            _driveService ??= await GetServiceAsync();

            var metadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = desiredFileName,
                Parents = new List<string> { destinationFolderId },
            };

            var request = _driveService.Files.Copy(metadata, sourceFileId);
            request.Fields = "id,name";
            var copied = await request.ExecuteAsync();
            return copied.Id;
        }

        public async Task DownloadFileAsync(string fileId, string saveToPath)
        {
            var request = _driveService!.Files.Get(fileId);
            using (var stream = new MemoryStream())
            {
                await request.DownloadAsync(stream);
                using (var fileStream = new FileStream(saveToPath, FileMode.Create, FileAccess.Write))
                {
                    stream.WriteTo(fileStream);
                }
            }
        }

        public async Task DownloadFolderAsZipAsync(
            string folderId,
            string zipFilePath,
            IProgress<TransferProgressUpdate>? stageProgress = null,
            CancellationToken cancellationToken = default)
        {
            _driveService ??= await GetServiceAsync(cancellationToken);

            var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var folderName = await GetDriveItemNameAsync(folderId, cancellationToken);
                if (string.IsNullOrWhiteSpace(folderName))
                    folderName = folderId;

                var safeFolderName = MakeSafePathName(folderName);
                var stagedRoot = Path.Combine(tempRoot, safeFolderName);
                Directory.CreateDirectory(stagedRoot);

                stageProgress?.Report(new TransferProgressUpdate(TransferStage.DownloadingFolder, 35));
                await DownloadFilesInFolderRecursiveAsync(folderId, stagedRoot, cancellationToken);

                stageProgress?.Report(new TransferProgressUpdate(TransferStage.ZippingFolder, 70));
                CreateZip(tempRoot, zipFilePath);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
        }

        private async Task DownloadFilesInFolderRecursiveAsync(string folderId, string destinationFolder, CancellationToken cancellationToken)
        {
            _driveService ??= await GetServiceAsync(cancellationToken);

            var request = _driveService.Files.List();
            request.Q = $"'{folderId}' in parents and trashed=false";
            request.Fields = "files(id, name, mimeType)";

            var result = await request.ExecuteAsync(cancellationToken);
            foreach (var file in result.Files)
            {
                var safeName = MakeSafePathName(file.Name);
                if (file.MimeType == "application/vnd.google-apps.folder")
                {
                    var childPath = Path.Combine(destinationFolder, safeName);
                    Directory.CreateDirectory(childPath);
                    await DownloadFilesInFolderRecursiveAsync(file.Id, childPath, cancellationToken);
                    continue;
                }

                var filePath = Path.Combine(destinationFolder, safeName);
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await _driveService.Files.Get(file.Id).DownloadAsync(fileStream, cancellationToken);
            }
        }

        private async Task<string> GetDriveItemNameAsync(string itemId, CancellationToken cancellationToken)
        {
            _driveService ??= await GetServiceAsync(cancellationToken);
            var request = _driveService.Files.Get(itemId);
            request.Fields = "name";
            var item = await request.ExecuteAsync(cancellationToken);
            return item.Name ?? string.Empty;
        }

        private static string MakeSafePathName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "untitled";

            foreach (var ch in Path.GetInvalidFileNameChars())
                name = name.Replace(ch, '_');

            return name;
        }

        private static void CreateZip(string sourceFolderPath, string destinationZipFilePath)
        {
            using var archive = ZipArchive.Create();
            AddFilesToArchive(archive, sourceFolderPath, sourceFolderPath);

            using var stream = File.Open(destinationZipFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            archive.SaveTo(stream, CompressionType.Deflate);
        }

        private static void AddFilesToArchive(ZipArchive archive, string rootPath, string currentPath)
        {
            foreach (var filePath in Directory.GetFiles(currentPath))
            {
                var relativePath = Path.GetRelativePath(rootPath, filePath);
                archive.AddEntry(relativePath, filePath);
            }

            foreach (var subDirPath in Directory.GetDirectories(currentPath))
                AddFilesToArchive(archive, rootPath, subDirPath);
        }

        public async Task DeleteFileAsync(string fileId)
        {
            var request = _driveService!.Files.Delete(fileId);
            await request.ExecuteAsync();
        }

        public async Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesAsync(string directoryId)
        {
            var request = _driveService!.Files.List();
            request.Q = $"'{directoryId}' in parents";
            request.Fields = "files(id, name, size, mimeType)";
            var result = await request.ExecuteAsync();
            return result.Files;
        }

        public async Task<ObservableCollection<WatcherFile>> GetFileDataCollectionAsync(string directoryId)
        {
            var files = await ListFilesAsync(directoryId);
            var fileDataCollection = new ObservableCollection<WatcherFile>();

            foreach (var file in files)
            {
                var fileData = await ConvertToFileDataAsync(file);
                fileDataCollection.Add(fileData);
            }

            return fileDataCollection;
        }

        private async Task<WatcherFile> ConvertToFileDataAsync(Google.Apis.Drive.v3.Data.File file)
        {
            var fileType = DetermineFileType(file.Name);
            var imageFile = GetIconForFileType(fileType);

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
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

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
            var iconFileName = fileType switch
            {
                WatcherFileType.Rar => "rar.png",
                WatcherFileType.Zip => "zip.png",
                WatcherFileType.Con => "rb.png",
                WatcherFileType.SevenZip => "sevenzip.png",
                WatcherFileType.CloneHero => "clonehero.png",
                _ => "blank.png",
            };

            return $"avares://RhythmVerseClient/Resources/Images/{iconFileName}";
        }

        private async Task<long> GetFileSizeAsync(string fileId)
        {
            var request = _driveService!.Files.Get(fileId);
            request.Fields = "size";
            var file = await request.ExecuteAsync();
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
            await StopMonitoringAsync().ConfigureAwait(false);
            _driveService?.Dispose();
            _driveService = null;
            _credential = null;
            RhythmVerseFolderId = string.Empty;
        }
    }
}