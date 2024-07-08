using Microsoft.Maui.Controls;
using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;
using RhythmVerseClient.Pages;
using RhythmVerseClient.Platforms.Windows;

namespace RhythmVerseClient.Pages
{
    public partial class MainPage : TabbedPage
    {
        private IWindowSizeService _windowSizeService;
        private double _previousWidth;
        private double _previousHeight;

        public MainPage(MainViewModel mainView, DownloadViewModel downView, CloneHeroViewModel cloneView, InstallSongViewModel installView, IWindowSizeService windowSizeService)
        {
            _windowSizeService = windowSizeService;
            InitializeComponent();
            BindingContext = mainView;
            _previousWidth = 0;
            _previousHeight = 0;
            var DownloadsPage = new DownloadPage(downView);
            Children.Add(DownloadsPage);
            var CloneHeroPage = new CloneHeroPage(cloneView);
            Children.Add(CloneHeroPage);
            var InstallSongPage = new InstallSongPage(installView, windowSizeService);
            Children.Add(InstallSongPage);

            CurrentPage = Children[0];
        }

        public void FocusOnTab(int tabIndex)
        {
            if (tabIndex >= 0 && tabIndex < Children.Count)
            {
                CurrentPage = Children[tabIndex];
            }
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width != _previousWidth || height != _previousHeight)
            {
                _previousWidth = width;
                _previousHeight = height;
                _windowSizeService.Refresh();
                DownloadPage? downloadPage = Children[0] as DownloadPage;

                if (downloadPage != null)
                {
                    downloadPage.ForceLayout();
                }
            }
        }

        private void OnButtonClicked(object sender, EventArgs e)
        {
            // Handle button click event
            DisplayAlert("Alert", "Button was clicked!", "OK");
        }
    }
}
