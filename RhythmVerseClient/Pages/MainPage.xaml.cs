using RhythmVerseClient.Api;
using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Pages
{
    public partial class MainPage : TabbedPage
    {
        

        public MainPage(MainViewModel mainView, DownloadViewModel downView, CloneHeroViewModel cloneView, InstallSongViewModel installView, RhythmVerseModel verseModel)
        {
            InitializeComponent();
            BindingContext = mainView;
            var RhythmVersePage = new RhythmVersePage(verseModel);
            Children.Add(RhythmVersePage);
            var DownloadsPage = new DownloadPage(downView);
            Children.Add(DownloadsPage);
            var CloneHeroPage = new CloneHeroPage(cloneView);
            Children.Add(CloneHeroPage);
            var InstallSongPage = new InstallSongPage(installView);
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

        private void OnButtonClicked(object sender, EventArgs e)
        {
            // Handle button click event
            DisplayAlert("Alert", "Button was clicked!", "OK");
        }
    }
}
