using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using ChartHub.Models;
using ChartHub.Utilities;

namespace ChartHub.Services
{
    /// <summary>
    /// An <see cref="IResourceWatcher"/> backed by a Google Drive folder.
    /// Loads files on construction and then polls at <see cref="PollingInterval"/>,
    /// adding/removing entries in <see cref="Data"/> to match the remote folder.
    /// </summary>
    public class GoogleDriveWatcher : IResourceWatcher, INotifyPropertyChanged, IAsyncDisposable
    {
        private readonly IGoogleDriveClient _driveClient;
        private readonly CancellationTokenSource _cts = new();
        private Task? _pollTask;

        public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

        // IResourceWatcher.DirectoryPath surfaces the Drive folder ID.
        public string DirectoryPath => _driveClient.ChartHubFolderId;

        private ObservableCollection<WatcherFile> _data = [];
        public ObservableCollection<WatcherFile> Data
        {
            get => _data;
            set { _data = value; OnPropertyChanged(); }
        }

        public event EventHandler<string>? DirectoryNotFound;

        public GoogleDriveWatcher(IGoogleDriveClient driveClient)
        {
            _driveClient = driveClient;
        }

        /// <summary>
        /// Performs an initial load then starts background polling.
        /// Call this once after construction (e.g. from InitializeAsync in the ViewModel).
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_driveClient.ChartHubFolderId))
            {
                Logger.LogWarning("Drive", "Google Drive watcher start skipped because folder is not initialised");
                DirectoryNotFound?.Invoke(this, "ChartHub folder not initialised on Google Drive.");
                return;
            }

            Logger.LogInfo("Drive", "Google Drive watcher starting", new Dictionary<string, object?>
            {
                ["folderId"] = _driveClient.ChartHubFolderId,
                ["pollingIntervalSeconds"] = PollingInterval.TotalSeconds,
            });
            _ = LoadItemsAsync(cancellationToken);
            _pollTask = PollAsync(_cts.Token);
        }

        // Synchronous IResourceWatcher.LoadItems() — kicks off an async load and returns immediately.
        public void LoadItems()
        {
            if (string.IsNullOrWhiteSpace(_driveClient.ChartHubFolderId))
            {
                Logger.LogWarning("Drive", "Google Drive watcher load skipped because folder is not initialised");
                DirectoryNotFound?.Invoke(this, "ChartHub folder not initialised on Google Drive.");
                return;
            }

            _ = LoadItemsAsync(CancellationToken.None);
        }

        private async Task LoadItemsAsync(CancellationToken cancellationToken)
        {
            IList<Google.Apis.Drive.v3.Data.File> files;
            try
            {
                files = await _driveClient.ListFilesAsync(_driveClient.ChartHubFolderId);
            }
            catch (Exception ex)
            {
                Logger.LogError("Drive", "Google Drive watcher failed to list files", ex, new Dictionary<string, object?>
                {
                    ["folderId"] = _driveClient.ChartHubFolderId,
                });
                return;
            }

            var mergedItems = await BuildMergedItemsAsync(Data, files);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var mergedIds = mergedItems
                    .Select(item => item.FilePath)
                    .ToHashSet(StringComparer.Ordinal);

                // Remove entries that are no longer on Drive.
                var toRemove = Data.Where(w => !mergedIds.Contains(w.FilePath)).ToList();
                foreach (var item in toRemove)
                    Data.Remove(item);

                // Add newly discovered entries while preserving retained instances.
                var existingIds = Data.Select(w => w.FilePath).ToHashSet(StringComparer.Ordinal);
                foreach (var item in mergedItems)
                {
                    if (existingIds.Contains(item.FilePath))
                        continue;

                    Data.Add(item);
                }
            });
        }

        private static async Task<IReadOnlyList<WatcherFile>> BuildMergedItemsAsync(
            IEnumerable<WatcherFile> currentItems,
            IList<Google.Apis.Drive.v3.Data.File> files)
        {
            var currentById = currentItems
                .Where(item => !string.IsNullOrWhiteSpace(item.FilePath))
                .GroupBy(item => item.FilePath, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var merged = new List<WatcherFile>(files.Count);
            foreach (var file in files)
            {
                if (!string.IsNullOrWhiteSpace(file.Id) && currentById.TryGetValue(file.Id, out var existing))
                {
                    merged.Add(existing);
                    continue;
                }

                merged.Add(await BuildWatcherFileAsync(file));
            }

            return merged;
        }

        private async Task PollAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(PollingInterval, cancellationToken);
                    await LoadItemsAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Logger.LogInfo("Drive", "Google Drive watcher polling cancelled", new Dictionary<string, object?>
                    {
                        ["folderId"] = _driveClient.ChartHubFolderId,
                    });
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError("Drive", "Google Drive watcher poll error", ex, new Dictionary<string, object?>
                    {
                        ["folderId"] = _driveClient.ChartHubFolderId,
                    });
                }
            }
        }

        private static async Task<WatcherFile> BuildWatcherFileAsync(Google.Apis.Drive.v3.Data.File file)
        {
            var fileType = DetermineFileType(file.Name);
            var imageFile = GetIconForFileType(fileType);

            // Size is included via ListFilesAsync which requests fields(id, name, size, mimeType).
            long sizeBytes = file.Size ?? 0;

            return await Task.FromResult(new WatcherFile(
                displayName: file.Name,
                filePath: file.Id,
                watcherFileType: fileType,
                imageFile: imageFile,
                sizeBytes: sizeBytes));
        }

        private static WatcherFileType DetermineFileType(string fileName)
        {
            return Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".zip" => WatcherFileType.Zip,
                ".rar" => WatcherFileType.Rar,
                ".7z" => WatcherFileType.SevenZip,
                "" => WatcherFileType.Con,
                _ => WatcherFileType.Unknown,
            };
        }

        private static string GetIconForFileType(WatcherFileType fileType)
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
            return $"avares://ChartHub/Resources/Images/{iconFileName}";
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            if (_pollTask is not null)
            {
                try { await _pollTask; } catch (OperationCanceledException) { }
            }
            _cts.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
