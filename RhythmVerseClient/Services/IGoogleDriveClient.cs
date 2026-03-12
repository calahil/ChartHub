using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using RhythmVerseClient.Models;
using System.Collections.ObjectModel;

namespace RhythmVerseClient.Services
{
    public interface IGoogleDriveClient
    {
        Task<string> CreateDirectoryAsync(string directoryName);
        Task<string> GetDirectoryIdAsync(string directoryName);
        Task<string> UploadFileAsync(string directoryId, string filePath);
        Task DownloadFileAsync(string fileId, string saveToPath);
        Task DeleteFileAsync(string fileId);
        Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesAsync(string directoryId);
        Task MonitorDirectoryAsync(string directoryId, TimeSpan pollingInterval, Action<Google.Apis.Drive.v3.Data.File, string> onFileChanged, CancellationToken cancellationToken = default);
        public string RhythmVerseFolderId { get; }

        Task InitializeAsync(CancellationToken cancellationToken = default);
        Task<ObservableCollection<WatcherFile>> GetFileDataCollectionAsync(string directoryId);
    }

    public class GoogleDriveClient : IGoogleDriveClient
    {
        private DriveService? _driveService;
        private readonly IConfiguration _configuration;
        private CancellationTokenSource? _monitorCts;
        private static readonly string[] Scopes = [Google.Apis.Drive.v3.DriveService.Scope.Drive];
        private static readonly string ApplicationName = "RhythmVerseClient";
        public string RhythmVerseFolderId { get; private set; } = string.Empty;
        static string credPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".credentials/drive-dotnet-maui.json");

        public GoogleDriveClient(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _driveService = await GetServiceAsync(cancellationToken);
            RhythmVerseFolderId = await CreateDirectoryAsync("RhythmVerse");
            _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = MonitorDirectoryAsync(RhythmVerseFolderId, TimeSpan.FromSeconds(30), OnFileChanged, _monitorCts.Token);
        }

        public async Task<DriveService> GetServiceAsync(CancellationToken cancellationToken = default)
        {
            UserCredential credential;
            var clientSecrets = new ClientSecrets
            {
                ClientId = _configuration["GoogleDrive:client_id"],
                ClientSecret = _configuration["GoogleDrive:client_secret"]
            };

            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                Scopes,
                "user",
                cancellationToken,
                new FileDataStore(credPath, true));

            return new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });
        }

        private void OnFileChanged(Google.Apis.Drive.v3.Data.File file, string changeType)
        {
            if (changeType == "created")
            {
                Console.WriteLine($"File created: {file.Name}");
            }
            else if (changeType == "deleted")
            {
                Console.WriteLine($"File deleted: {file.Name}");
            }
        }

        public async Task MonitorDirectoryAsync(string directoryId, TimeSpan pollingInterval, Action<Google.Apis.Drive.v3.Data.File, string> onFileChanged, CancellationToken cancellationToken = default)
        {
            var existingFiles = new Dictionary<string, Google.Apis.Drive.v3.Data.File>();

            while (!cancellationToken.IsCancellationRequested)
            {
                var files = await ListFilesAsync(directoryId);

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

                await Task.Delay(pollingInterval, cancellationToken);
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
            return file.Id;
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

            return folder?.Id;
        }

        public async Task<string> UploadFileAsync(string directoryId, string filePath)
        {
            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = Path.GetFileName(filePath),
                Parents = new List<string> { directoryId }
            };

            using (var stream = new FileStream(filePath, FileMode.Open))
            {
                var request = _driveService!.Files.Create(fileMetadata, stream, GetMimeType(filePath));
                request.Fields = "id";
                await request.UploadAsync();
                var file = request.ResponseBody;
                return file.Id;
            }
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
    }
}