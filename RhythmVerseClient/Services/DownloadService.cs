using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using RhythmVerseClient.Api;
using RhythmVerseClient.Utilities;
using RhythmVerseClient.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.UI.StartScreen;

namespace RhythmVerseClient.Services
{
    public class DownloadService
    {
        private readonly HttpClient _httpClient;
        private readonly UrlHelper _urlHelper;
        private readonly GoogleDriveService _googleDriveService;
        private IProgress<double> _progress;

        public DownloadService(IConfiguration configuration)
        {
            _httpClient = new HttpClient();
            _urlHelper = new UrlHelper();
            _googleDriveService = new GoogleDriveService(configuration);
            _progress = new Progress<double>();
        }

        public async Task DownloadFileAsync(DownloadFile song)
        {
            try
            {
                string finalUrl = await _urlHelper.GetFinalRedirectUrlAsync(song.Url);
                song.Url = finalUrl;
                if (finalUrl.StartsWith("https://drive.google.com/drive"))
                {

                }
                else if (finalUrl.StartsWith("https://drive.google.com/file"))
                {
                    var fileId = UrlExtractor.ExtractIdFromUrl(finalUrl);
                    await _googleDriveService.DownloadFileAsync(song, _progress, fileId);                    
                }
                else
                {
                    var response = await _httpClient.GetAsync(finalUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var canReportProgress = totalBytes != -1;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        var totalRead = 0L;
                        var buffer = new byte[8192];
                        var isMoreToRead = true;

                        using (var fileStream = new FileStream(Path.Combine(song.FilePath, song.DisplayName), FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true))
                        {
                            do
                            {
                                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                                if (read == 0)
                                {
                                    isMoreToRead = false;
                                    TriggerProgressChanged(song, totalBytes, totalRead);
                                    continue;
                                }

                                await fileStream.WriteAsync(buffer.AsMemory(0, read));

                                totalRead += read;

                                if (canReportProgress)
                                {
                                    TriggerProgressChanged(song, totalBytes, totalRead);
                                }
                            }
                            while (isMoreToRead);
                        }
                    }

                    song.Finished = true;
                    //byte[] data = await _httpClient.GetByteArrayAsync(song.Url);
                    //await System.IO.File.WriteAllBytesAsync(Toolbox.ConstructPath(song.FilePath, song.DisplayName), data);
                }
            }
            catch (Exception ex)
            {
                Logger.LogMessage($"An error occurred: {ex.Message}");
            }
        }

        private void TriggerProgressChanged(DownloadFile song, long totalDownloadSize, long totalBytesRead)
        {
            if (totalDownloadSize != -1)
            {
                song.DownloadProgress = (double)totalBytesRead / totalDownloadSize * 100;
            }
        }
    }

    public class GoogleDriveService
    {
        private static readonly string[] Scopes = { DriveService.Scope.DriveReadonly };
        private static readonly string ApplicationName = "RhythmVerseClient";
        private readonly IConfiguration _configuration;

        public GoogleDriveService(IConfiguration configuration)
        {
            _configuration = configuration;
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
            new FileDataStore("token.json", false));

            return new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });
        }

        public async Task DownloadFileAsync(DownloadFile downloadFile, IProgress<double> progress, string fileId, CancellationToken cancellationToken = default)
        {
            var service = await GetServiceAsync();
            var request = service.Files.Get(fileId);
            var savePath = Toolbox.ConstructPath(downloadFile.FilePath, downloadFile.DisplayName);

            

            using (var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var file = await request.DownloadAsync(fileStream);
                downloadFile.FileSize = file.BytesDownloaded; // Update file size
                var mediaDownloader = new MediaDownloader(service)
                {
                    ChunkSize = 256 * 1024  // Adjust the chunk size if needed
                };

                mediaDownloader.ProgressChanged += progressEvent =>
                {
                    if (progressEvent.Status == DownloadStatus.Downloading)
                    {
                        double progressPercentage = (double)progressEvent.BytesDownloaded / downloadFile.FileSize * 100;
                        progress?.Report(progressPercentage); // Correctly reporting progress
                        downloadFile.DownloadProgress = progressPercentage;
                    }
                    else if (progressEvent.Status == DownloadStatus.Completed)
                    {
                        downloadFile.Finished = true;
                        downloadFile.DownloadProgress = 100;
                    }
                };

                await mediaDownloader.DownloadAsync(downloadFile.Url, fileStream, cancellationToken);
            }
        }

        public async Task<List<Google.Apis.Drive.v3.Data.File>> ListFilesInFolder(string folderId)
        {
            var service = await GetServiceAsync();
            var request = service.Files.List();
            request.Q = $"'{folderId}' in parents";
            request.Fields = "nextPageToken, files(id, name)";

            var result = await request.ExecuteAsync();
            return result.Files.ToList();
        }

        /*public async Task DownloadFolderAsZip(string folderId, string savePath)
        {
            var files = await ListFilesInFolder(folderId);
            using (var memoryStream = new MemoryStream())
            {
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    foreach (var file in files)
                    {
                        var fileEntry = archive.CreateEntry(file.Name);
                        using (var entryStream = fileEntry.Open())
                        using (var fileContentStream = new MemoryStream())
                        {
                            await DownloadFileAsync(file.Id, fileContentStream);
                            fileContentStream.Seek(0, SeekOrigin.Begin);
                            fileContentStream.CopyTo(entryStream);
                        }
                    }
                }

                using (var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    memoryStream.CopyTo(fileStream);
                }
            }
        }*/

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
                using (var response = await _httpClient.GetAsync(finalUrl))
                {
                    isRedirect = (int)response.StatusCode >= 300 && (int)response.StatusCode < 400;
                    if (isRedirect)
                    {
                        finalUrl = response.Headers.Location.IsAbsoluteUri
                            ? response.Headers.Location.AbsoluteUri
                            : new Uri(new Uri(finalUrl), response.Headers.Location).AbsoluteUri;
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

    public class DownloadFile(string displayName, string filePath, string urlString, long fileSize) : INotifyPropertyChanged
    {
        private string _displayName = displayName;
        private string _filePath = filePath;
        private string _url = urlString;
        private double _downloadProgress = 0;
        private bool _finished = false;
        private long _fileSize = fileSize;

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

        public double DownloadProgress
        {
            get => _downloadProgress;
            set
            {
                _downloadProgress = value;
                OnPropertyChanged(nameof(DownloadProgress));
            }
        }

        public bool Finished
        {
            get => _finished;
            set
            {
                _finished = value;
                OnPropertyChanged(nameof(Finished));
            }
        }

        public long FileSize
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
