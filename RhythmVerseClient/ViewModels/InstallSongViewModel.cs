using CommunityToolkit.Mvvm.Input;
using RhythmVerseClient.Services;
using RhythmVerseClient.Strings;
using RhythmVerseClient.Utilities;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace RhythmVerseClient.ViewModels
{
    public class InstallSongViewModel : INotifyPropertyChanged
    {
        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set
            {
                _progressValue = value;
                OnPropertyChanged();
            }
        }

        private string _details;
        public string Details
        {
            get => _details;
            set
            {
                _details = value;
                OnPropertyChanged();
            }
        }

        public IAsyncRelayCommand StartBarCommand { get; }
        public ICommand GoBackCommand { get; }

        public InstallPageStrings PageString { get; }

        public IResourceWatcher OnyxWatcher { get; set; }

        private readonly AppGlobalSettings globalSettings;

        public InstallSongViewModel(AppGlobalSettings settings)
        {
            _progressValue = 0;
            _details = String.Empty;
            StartBarCommand = new AsyncRelayCommand(StartBar);
            GoBackCommand = new RelayCommand(GoBack);

            PageString = new InstallPageStrings();
            globalSettings = settings;

            OnyxWatcher = new ResourceWatcher(globalSettings.StagingDir, WatcherType.File);
        }

        private void GoBack()
        {
            Toolbox.DebugResetSongProcessor(globalSettings.TempDir, globalSettings.DownloadDir, globalSettings.TempDir);
            // TODO: Convert UI tab navigation to Avalonia
            // var mainPage = Application.Current?.MainPage as MainPage;
            // mainPage?.FocusOnTab(1);
        }

        private async Task StartBar()
        {
            Details = String.Empty;
            ProgressValue = 0;
            Details += PageString.StartProcess;
            var processedFiles = new List<string>();
            var totalFiles = OnyxWatcher.Data.Count + 1; // +1 to account for Cleanup step

            await Task.Delay(100);
            double progressIncrement = 1.0;
            foreach (var song in OnyxWatcher.Data)
            {
                var extension = Path.GetExtension(song.FilePath).ToLower();
                processedFiles.Add(song.FilePath);
                if (extension == ".zip" || extension == ".rar" || extension == ".7z")
                {
                    Details += extension switch
                    {
                        ".zip" => PageString.UnzipFile.FormatString(song.DisplayName),
                        ".rar" => PageString.UnRarFile.FormatString(song.DisplayName),
                        ".7z" => PageString.ExtractFile.FormatString(song.DisplayName),
                        _ => PageString.ExtractFile.FormatString(song.DisplayName),
                    };
                    await Task.Delay(100);
                    using var archive = Toolbox.OpenArchive(song.FilePath);
                    foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                    {
                        entry.WriteToDirectory(globalSettings.OutputDir, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                        var progress = progressIncrement / totalFiles;
                        ProgressValue = progress;
                        progressIncrement += 1.0;
                    }
                    Details += PageString.Done;
                    await Task.Delay(100);
                }
                else
                {
                    Details += PageString.OnyxImport.FormatString(song.DisplayName);
                    Details += PageString.OnyxBuild.FormatString(song.DisplayName);
                    Onyx onyx = new Onyx(globalSettings, song.FilePath);
                    Details += PageString.OnyxFinish;
                    var progress = progressIncrement / totalFiles;
                    ProgressValue = progress;
                    progressIncrement += 1.0;
                }
            }


            await Task.Delay(100);
            Details += PageString.InstallSongs;
            Toolbox.MoveDirectory(globalSettings.OutputDir, globalSettings.CloneHeroSongsDir);

            foreach (string song in processedFiles)
            {
                try
                {
                    File.Delete(song);
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"An error occurred: {ex.Message}");
                }
            }

            Details += PageString.Finished;

        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
