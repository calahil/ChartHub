using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using RhythmVerseClient.Utilities;
using RhythmVerseClient.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RhythmVerseClient.Services
{
    public class DownloadService
    {
        private readonly HttpClient _httpClient;
        private readonly UrlHelper _urlHelper;
        private readonly GoogleDriveService _googleDriveService;
        private IProgress<double> _progress;

        public DownloadService(IConfiguration configuration, IProgress<double> progress)
        {
            _httpClient = new HttpClient();
            _urlHelper = new UrlHelper();
            _googleDriveService = new GoogleDriveService(configuration);
            _progress = progress;
        }

        public async Task DownloadFileAsync(DownloadFile song)
        {
            try
            {
                string finalUrl = await _urlHelper.GetFinalRedirectUrlAsync(song.Url);

                if (finalUrl.StartsWith("https://drive.google.com/drive"))
                {

                }
                else if (finalUrl.StartsWith("https://drive.google.com/file"))
                {
                    var fileId = UrlExtractor.ExtractIdFromUrl(finalUrl);
                    await _googleDriveService.DownloadFileAsync(fileId, Toolbox.ConstructPath(song.FilePath, song.DisplayName), _progress);                    
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

        public async Task DownloadFileAsync(string fileId, string savePath, IProgress<double> progressReporter)
        {
            var service = await GetServiceAsync();
            var request = service.Files.Get(fileId);

            // Get the file to determine total size for progress calculation
            var file = await service.Files.Get(fileId).ExecuteAsync();
            long? totalSize = file.Size;  // Total size of the file.

            using (var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                // Create a progress stream that reports the download progress.
                var progressStream = new ProgressStream(fileStream, progressReporter, totalSize.Value);
                request.MediaDownloader.ProgressChanged += (IDownloadProgress prog) =>
                {
                    if (prog.Status == DownloadStatus.Downloading)
                    {
                        progressStream.Report(prog.BytesDownloaded);
                    }
                    else if (prog.Status == DownloadStatus.Completed)
                    {
                        progressReporter.Report(100);  // Ensure that progress is reported as 100% at the end
                    }
                };

                // Download directly into the file stream.
                await request.DownloadAsync(progressStream);
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

    public class DownloadFile(string displayName, string filePath, string urlString) : INotifyPropertyChanged
    {
        private string _displayName = displayName;
        private string _filePath = filePath;
        private string _url = urlString;
        private double _downloadProgress = 0;
        private bool _finished = false;

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

    class ProgressStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly IProgress<double> _progressReporter;
        private readonly long _totalSize;
        private long _totalRead;

        public ProgressStream(Stream baseStream, IProgress<double> progressReporter, long totalSize)
        {
            _baseStream = baseStream;
            _progressReporter = progressReporter;
            _totalSize = totalSize;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _baseStream.Write(buffer, offset, count);
            _totalRead += count;
            Report(_totalRead);
        }

        public void Report(long bytesDownloaded)
        {
            _progressReporter.Report((double)bytesDownloaded / _totalSize * 100);
        }

        // Implement all other abstract methods and pass-through calls to _baseStream

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => _baseStream.CanSeek;
        public override bool CanWrite => _baseStream.CanWrite;
        public override long Length => _baseStream.Length;

        public override long Position
        {
            get => _baseStream.Position;
            set => _baseStream.Position = value;
        }

        public override void Flush() => _baseStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _baseStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _baseStream.Seek(offset, origin);
        public override void SetLength(long value) => _baseStream.SetLength(value);
    }
}
