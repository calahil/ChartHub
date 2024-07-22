using CommunityToolkit.Mvvm.Input;
using RhythmVerseClient.Pages;
using RhythmVerseClient.Platforms.Windows;
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

        private double _consoleHeight;
        public double ConsoleHeight
        {
            get => _consoleHeight;
            set
            {
                _consoleHeight = value;
                OnPropertyChanged();
            }
        }

        public IAsyncRelayCommand StartBarCommand { get; }
        public ICommand GoBackCommand { get; }

        private readonly IWindowSizeService _windowSizeService;

        public InstallPageStrings PageString { get; }

        private IKeystrokeSender _keystrokeSender;

        public IResourceWatcher PhaseshiftWatcher { get; set; }

        private readonly AppGlobalSettings globalSettings;

        public InstallSongViewModel(AppGlobalSettings settings, IWindowSizeService windowSizeService, IKeystrokeSender keystrokeSender)
        {
            _progressValue = 0;
            _details = String.Empty;
            StartBarCommand = new AsyncRelayCommand(StartBar);
            GoBackCommand = new Command(GoBack);
            _windowSizeService = windowSizeService;
            _windowSizeService.PropertyChanged += WindowSizeService_PropertyChanged;

            _keystrokeSender = keystrokeSender;
            _consoleHeight = windowSizeService.GetWindowSize().Height;
            PageString = new InstallPageStrings();
            globalSettings = settings;

            PhaseshiftWatcher = new ResourceWatcher(globalSettings.PhaseshiftDir, WatcherType.File);
        }

        private void GoBack()
        {
            Toolbox.DebugResetSongProcessor(globalSettings.PhaseshiftDir, globalSettings.DownloadDir, globalSettings.PhaseshiftMusicDir);
            var mainPage = Application.Current?.MainPage as MainPage;
            mainPage?.FocusOnTab(1);
        }

        private void WindowSizeService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var temp = _windowSizeService.Height - ((128 * 2) + 88);
            if (temp < 0)
            {
                temp *= -1;
            }
            if (ConsoleHeight != temp)
            {
                ConsoleHeight = temp;
            }
        }

        private async Task StartBar()
        {
            Details = String.Empty;
            ProgressValue = 0;
            Details += PageString.StartProcess;
            var processedFiles = new List<string>();

            await Task.Delay(100);
            foreach (var song in PhaseshiftWatcher.Data)
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
                        entry.WriteToDirectory(globalSettings.PhaseshiftMusicDir, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                    Details += PageString.Done;
                    await Task.Delay(100);
                }
            }

            Details += PageString.StartNautilus;

            await Task.Delay(100);
            //while (ProgressValue < 1)
            //{
            //    ProgressValue += 0.01;
            //    Details += ProgressValue.ToString() + "%" + Environment.NewLine;
            //    await Task.Delay(100);
            //}
            var nautilus = new Nautilus(_keystrokeSender, globalSettings.NautilusDirectoryPath);
            Details += PageString.NautilusConversion;
            await Task.Delay(100);
            await nautilus.RunAsync();
            Details += PageString.StopNautilus;
            await Task.Delay(100);
            Details += PageString.InstallSongs;
            Toolbox.MoveDirectory(globalSettings.PhaseshiftMusicDir, globalSettings.CloneHeroSongsDir);

            foreach (string song in processedFiles)
            {
                try
                {
                    File.Delete(song);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex);
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
