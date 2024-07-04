using Microsoft.Maui.Controls;
using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;
using RhythmVerseClient.Pages;
using RhythmVerseClient.Platforms.Windows;

namespace RhythmVerseClient
{
    public partial class MainPage : TabbedPage
    {
        private IWindowSizeService _windowSizeService;

        public MainPage(MainViewModel mainView, DownloadViewModel downView, CloneHeroViewModel cloneView, InstallSongViewModel installView, IWindowSizeService windowSizeService)
        {
            _windowSizeService = windowSizeService;
            InitializeComponent();
            BindingContext = mainView;

            var DownloadsPage = new DownloadPage(downView);
            Children.Add(DownloadsPage);
            var CloneHeroPage = new CloneHeroPage(cloneView);
            Children.Add(CloneHeroPage);
            var InstallSongPage = new InstallSongPage(installView, windowSizeService);
            Children.Add(InstallSongPage);
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            _windowSizeService.Refresh();
        }

        private void OnButtonClicked(object sender, EventArgs e)
        {
            // Handle button click event
            DisplayAlert("Alert", "Button was clicked!", "OK");
        }
    }
}
