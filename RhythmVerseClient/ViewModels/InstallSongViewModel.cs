using CommunityToolkit.Mvvm.Input;
using RhythmVerseClient.Platforms.Windows;
using RhythmVerseClient.Services;
using RhythmVerseClient.Strings;
using RhythmVerseClient.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace RhythmVerseClient.ViewModels
{
    public class InstallSongViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<FileData> _installSongs;
        public ObservableCollection<FileData> InstallSongs
        {
            get => _installSongs;
            set
            {
                _installSongs = value;
                OnPropertyChanged();
            }
        }

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

        private readonly IWindowSizeService _windowSizeService;

        public InstallPageStrings PageString { get; }

        private IKeystrokeSender _keystrokeSender;

        public InstallSongViewModel(AppGlobalSettings settings, IWindowSizeService windowSizeService, IKeystrokeSender keystrokeSender)
        {
            _progressValue = 0;
            _details = String.Empty;
            StartBarCommand = new AsyncRelayCommand(StartBar);

            _windowSizeService = windowSizeService;
            _windowSizeService.PropertyChanged += _windowSizeService_PropertyChanged;

            _keystrokeSender = keystrokeSender;
            _installSongs = [];
            _consoleHeight = windowSizeService.GetWindowSize().Height;
            PageString = new InstallPageStrings();
         
        }

        private void _windowSizeService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var temp = _windowSizeService.Height - ((128 * 2) + 88);
            if (temp < 0)
            {
                temp = temp * -1;
            }
            if (ConsoleHeight != temp)
            {
                ConsoleHeight = temp;
            }
        }

        private async Task StartBar()
        {
            ProgressValue = 0;
            while (ProgressValue < 1)
            {
                ProgressValue += 0.01;
                Details += ProgressValue.ToString() + "%" + Environment.NewLine;
                await Task.Delay(100);
            }

        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
