using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace RhythmVerseClient.Services
{
    public interface IGoogleDriveClient
    {
        Task<string> CreateDirectoryAsync(string directoryName);
        Task<string> UploadFileAsync(string directoryId, string filePath);
        Task DownloadFileAsync(string fileId, string saveToPath);
        Task DeleteFileAsync(string fileId);
        Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesAsync(string directoryId);
        public string RhythmVerseFolderId { get; }
    }

    public class GoogleDriveClient : IGoogleDriveClient
    {
        private DriveService? _driveService;
        private readonly IConfiguration _configuration;
        private static readonly string[] Scopes = [Google.Apis.Drive.v3.DriveService.Scope.Drive];
        private static readonly string ApplicationName = "RhythmVerseClient";
        public string RhythmVerseFolderId {  get; private set; }
        static string credPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".credentials/drive-dotnet-maui.json");

        public GoogleDriveClient(IConfiguration configuration)
        {
            _configuration = configuration;
            InitializeGoogleDriveService();
        }

        private async void InitializeGoogleDriveService()
        {
            _driveService = await GetServiceAsync();
            RhythmVerseFolderId = await CreateDirectoryAsync("RhythmVerse");
        }

        public async Task<DriveService> GetServiceAsync()
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
            CancellationToken.None,
            new FileDataStore(credPath, false));

            return new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });
        }

        public async Task<string> CreateDirectoryAsync(string directoryName)
        {
            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = directoryName,
                MimeType = "application/vnd.google-apps.folder"
            };
            var request = _driveService.Files.Create(fileMetadata);
            request.Fields = "id";
            var file = await request.ExecuteAsync();
            return file.Id;
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
                var request = _driveService.Files.Create(fileMetadata, stream, GetMimeType(filePath));
                request.Fields = "id";
                await request.UploadAsync();
                var file = request.ResponseBody;
                return file.Id;
            }
        }

        public async Task DownloadFileAsync(string fileId, string saveToPath)
        {
            var request = _driveService.Files.Get(fileId);
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
            var request = _driveService.Files.Delete(fileId);
            await request.ExecuteAsync();
        }

        public async Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesAsync(string directoryId)
        {
            var request = _driveService.Files.List();
            request.Q = $"'{directoryId}' in parents";
            request.Fields = "files(id, name)";
            var result = await request.ExecuteAsync();
            return result.Files;
        }

        private string GetMimeType(string fileName)
        {
            string mimeType = "application/unknown";
            string ext = Path.GetExtension(fileName).ToLower();
            Microsoft.Win32.RegistryKey regKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(ext);
            if (regKey != null && regKey.GetValue("Content Type") != null)
                mimeType = regKey.GetValue("Content Type").ToString();
            return mimeType;
        }
    }
}