using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using HtmlAgilityPack;
using ChartHub.Utilities;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace ChartHub.Services
{
    public class DownloadService(IGoogleAuthProvider authProvider)
    {
        private readonly HttpClient _httpClient = new();
        private readonly UrlHelper _urlHelper = new();
        private readonly GoogleDriveService _googleDriveService = new(authProvider);
        private IProgress<double> _progress = new Progress<double>();

        public async Task DownloadFileAsync(DownloadFile song, CancellationToken cancellationToken = default)
        {
            string finalUrl = await _urlHelper.GetFinalRedirectUrlAsync(song.Url);
            song.Url = finalUrl;

            if (song.Url.StartsWith("https://drive.google.com/drive"))
            {
                song.Status = "DownloadingDriveFolder";
                var fileId = UrlExtractor.ExtractIdFromUrl(song.Url);
                await _googleDriveService.DownloadFolderAsync(song, fileId, cancellationToken);
            }
            else if (song.Url.StartsWith("https://drive.google.com/file"))
            {
                song.Status = "DownloadingDriveFile";
                var fileId = UrlExtractor.ExtractIdFromUrl(song.Url);
                await _googleDriveService.DownloadFileAsync(song, _progress, fileId, cancellationToken);
            }
            else if (song.Url.StartsWith("https://www.mediafire.com") || song.Url.StartsWith("http://www.mediafire.com"))
            {
                song.Status = "ResolvingMediaFire";
                HttpResponseMessage response = await _httpClient.GetAsync(finalUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                string content = await response.Content.ReadAsStringAsync(cancellationToken);

                HtmlDocument doc = new();
                doc.LoadHtml(content);

                var linkNode = doc.DocumentNode.SelectSingleNode("//a[@id='downloadButton']");

                if (linkNode != null)
                {
                    string downloadLink = linkNode.GetAttributeValue("href", "");
                    song.Url = downloadLink;
                    song.DisplayName = ExtractMediaFireFileName(downloadLink);
                    await DownloadAsync(song, cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException("MediaFire download link not found on page.");
                }

            }
            else
            {
                await DownloadAsync(song, cancellationToken);
            }

            if (song.DownloadProgress != 100)
                song.DownloadProgress = 100;
            song.Finished = true;
        }

        private async Task DownloadAsync(DownloadFile song, CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync(song.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1;

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var totalRead = 0L;
            var buffer = new byte[8192];
            var isMoreToRead = true;
            var savePath = SafePathHelper.GetSafeFilePath(song.FilePath, song.DisplayName, "download.bin");
            song.DisplayName = Path.GetFileName(savePath);

            using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true);
            do
            {
                var read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    isMoreToRead = false;
                    TriggerProgressChanged(song, totalBytes, totalRead);
                    continue;
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                totalRead += read;

                if (canReportProgress)
                {
                    TriggerProgressChanged(song, totalBytes, totalRead);
                }
            }
            while (isMoreToRead);
        }

        private static string ExtractMediaFireFileName(string url)
        {
            Uri uri = new(url);

            // Get the absolute path of the URL
            string absolutePath = uri.AbsolutePath;

            // Extract the part after the last "/"
            return absolutePath[(absolutePath.LastIndexOf('/') + 1)..];
        }

        private static void TriggerProgressChanged(DownloadFile song, long totalDownloadSize, long totalBytesRead)
        {
            if (totalDownloadSize != -1)
            {
                song.DownloadProgress = (double)totalBytesRead / totalDownloadSize * 100;
            }
        }
    }

    public class GoogleDriveService(IGoogleAuthProvider authProvider)
    {
        private static readonly string[] Scopes = [DriveService.Scope.DriveReadonly, DriveService.Scope.DriveFile];
        private static readonly string ApplicationName = "ChartHub";
        private readonly IGoogleAuthProvider _authProvider = authProvider;
        private DriveService? _driveService;
        private UserCredential? _credential;

        public async Task<DriveService> GetServiceAsync(CancellationToken cancellationToken = default)
        {
            if (_driveService is not null)
                return _driveService;

            if (_credential is null)
            {
                _credential = await _authProvider.TryAuthorizeSilentAsync(Scopes, cancellationToken)
                    ?? await _authProvider.AuthorizeInteractiveAsync(Scopes, cancellationToken);
            }

            _driveService = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = _credential,
                ApplicationName = ApplicationName,
            });

            return _driveService;
        }

        public async Task DownloadFileAsync(DownloadFile downloadFile, IProgress<double> progress, string fileId, CancellationToken cancellationToken = default)
        {
            _driveService ??= await GetServiceAsync();

            var request = _driveService.Files.Get(fileId);
            var savePath = SafePathHelper.GetSafeFilePath(downloadFile.FilePath, downloadFile.DisplayName, "download.bin");
            downloadFile.DisplayName = Path.GetFileName(savePath);

            request.MediaDownloader.ChunkSize = 256 * 1024;
            request.MediaDownloader.ProgressChanged += progressEvent =>
            {
                if (progressEvent.Status == Google.Apis.Download.DownloadStatus.Downloading)
                {
                    if (downloadFile.FileSize is > 0)
                    {
                        double progressPercentage = (double)progressEvent.BytesDownloaded / downloadFile.FileSize.Value * 100;
                        progress?.Report(progressPercentage);
                        downloadFile.DownloadProgress = progressPercentage;
                    }
                }
                else if (progressEvent.Status == Google.Apis.Download.DownloadStatus.Completed)
                {
                    downloadFile.Finished = true;
                    downloadFile.DownloadProgress = 100;
                }
            };

            using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);
            var file = await request.DownloadAsync(fileStream, cancellationToken);
            downloadFile.FileSize = Math.Max(downloadFile.FileSize ?? 0, file.BytesDownloaded);
        }

        public async Task DownloadFolderAsync(DownloadFile downloadFile, string fileId, CancellationToken cancellationToken = default)
        {
            string folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(folderPath);
            var result = await GetFolderNameAsync(fileId);

            string finalPath = SafePathHelper.GetSafeFilePath(folderPath, result, "drive-folder");

            Directory.CreateDirectory(finalPath);
            await DownloadFilesInFolder(fileId, finalPath, cancellationToken);
            var zipFile = SafePathHelper.GetSafeFilePath(downloadFile.FilePath, downloadFile.DisplayName, "download.zip");
            downloadFile.DisplayName = Path.GetFileName(zipFile);
            CreateZip(folderPath, zipFile);

            Directory.Delete(folderPath, true); // Cleanup the temporary folder
        }

        private async Task DownloadFilesInFolder(string folderId, string parentFolderPath, CancellationToken cancellationToken = default)
        {
            _driveService ??= await GetServiceAsync();

            var request = _driveService.Files.List();
            request.Q = $"'{folderId}' in parents";
            request.Fields = "files(id, name, mimeType)";

            var result = await request.ExecuteAsync(cancellationToken);

            foreach (var file in result.Files)
            {
                if (file.MimeType == "application/vnd.google-apps.folder")
                {
                    string subFolderPath = SafePathHelper.GetSafeFilePath(parentFolderPath, file.Name, "folder");
                    Directory.CreateDirectory(subFolderPath);
                    await DownloadFilesInFolder(file.Id, subFolderPath, cancellationToken);
                }
                else
                {
                    var stream = new MemoryStream();
                    await _driveService.Files.Get(file.Id).DownloadAsync(stream, cancellationToken);
                    stream.Seek(0, SeekOrigin.Begin);

                    string filePath = SafePathHelper.GetSafeFilePath(parentFolderPath, file.Name, "download.bin");
                    using var fileStream = System.IO.File.Create(filePath);
                    stream.CopyTo(fileStream);
                }
            }
        }

        public async Task<string> GetFolderNameAsync(string folderId)
        {
            _driveService ??= await GetServiceAsync();
            try
            {
                var request = _driveService.Files.Get(folderId);
                request.Fields = "name"; // Specify that you only want the 'name' field

                var folder = await request.ExecuteAsync();
                return folder.Name;
            }
            catch (Exception ex)
            {
                Logger.LogError("Drive", "Failed to resolve Google Drive folder name", ex, new Dictionary<string, object?>
                {
                    ["folderId"] = folderId,
                });
                return string.Empty;
            }
        }


        private static void CreateZip(string folderPath, string destinationZipFilePath)
        {
            using var archive = ZipArchive.Create();
            // Recursively add files to the archive
            AddFilesToArchive(archive, folderPath, folderPath);

            // Save the archive to a file
            using var stream = System.IO.File.OpenWrite(destinationZipFilePath);
            archive.SaveTo(stream, CompressionType.Deflate);
        }

        private static void AddFilesToArchive(ZipArchive archive, string rootPath, string currentPath)
        {
            // Add files from the current directory to the archive
            foreach (var filePath in Directory.GetFiles(currentPath))
            {
                string relativePath = Path.GetRelativePath(rootPath, filePath);
                archive.AddEntry(relativePath, filePath);
            }

            // Recursively add files from subdirectories
            foreach (var subDirPath in Directory.GetDirectories(currentPath))
            {
                AddFilesToArchive(archive, rootPath, subDirPath);
            }
        }

    }

    public class UrlHelper
    {
        private readonly HttpClient _httpClient;

        public UrlHelper()
        {
            _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        }

        public async Task<string> GetFinalRedirectUrlAsync(string url)
        {
            string finalUrl = url;
            bool isRedirect;

            do
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, finalUrl);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                isRedirect = (int)response.StatusCode >= 300 && (int)response.StatusCode < 400;
                if (response != null)
                {
                    if (isRedirect)
                    {
                        if (response.Headers.Location != null)
                        {
                            finalUrl = response.Headers.Location.IsAbsoluteUri
                                ? response.Headers.Location.AbsoluteUri
                                : new Uri(new Uri(finalUrl), response.Headers.Location).AbsoluteUri;
                        }
                    }
                    else if ((int)response.StatusCode == 200)
                    {
                        break;
                    }
                    else
                    {
                        throw new HttpRequestException($"Unexpected response status code: {response.StatusCode}");
                    }
                }
            } while (isRedirect);

            return finalUrl;
        }
    }

    public class UrlExtractor
    {
        public static string ExtractIdFromUrl(string url)
        {
            // Regex pattern to match file ID in Google Drive URL
            var filePattern = @"/d/([a-zA-Z0-9_-]+)";
            var folderPattern = @"/folders/([a-zA-Z0-9_-]+)";

            // Match file ID
            var fileMatch = Regex.Match(url, filePattern);
            if (fileMatch.Success)
            {
                return fileMatch.Groups[1].Value;  // Groups[1] is the first captured group
            }

            // Match folder ID
            var folderMatch = Regex.Match(url, folderPattern);
            if (folderMatch.Success)
            {
                return folderMatch.Groups[1].Value;
            }

            // If no match found, throw an exception
            throw new ArgumentException("Invalid Google Drive URL");
        }

    }
    public class DownloadFile(string displayName, string filePath, string urlString, long? fileSize) : INotifyPropertyChanged
    {
        private string _displayName = displayName;
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        private string _filePath = filePath;
        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged(nameof(FilePath));
                }
            }
        }

        private string _url = urlString;
        public string Url
        {
            get => _url;
            set
            {
                if (_url != value)
                {
                    _url = value;
                    OnPropertyChanged(nameof(Url));
                }
            }
        }

        private double _downloadProgress = 0;
        public double DownloadProgress
        {
            get => _downloadProgress;
            set
            {
                _downloadProgress = value;
                OnPropertyChanged(nameof(DownloadProgress));
            }
        }

        private bool _finished = false;
        public bool Finished
        {
            get => _finished;
            set
            {
                _finished = value;
                OnPropertyChanged(nameof(Finished));
                OnPropertyChanged(nameof(CanCancel));
            }
        }

        private string _status = "Queued";
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(CanCancel));
                }
            }
        }

        public bool CanCancel => !Finished
            && !string.Equals(Status, "Cancelling", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Status, "Failed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Status, "Completed", StringComparison.OrdinalIgnoreCase);

        private string? _errorMessage;
        public string? ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged(nameof(ErrorMessage));
                }
            }
        }

        private long? _fileSize = fileSize;
        public long? FileSize
        {
            get => _fileSize;
            set
            {
                if (_fileSize != value)
                {
                    _fileSize = value;
                    OnPropertyChanged(nameof(FileSize));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return DisplayName;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(DisplayName, FilePath);
        }
    }
}

