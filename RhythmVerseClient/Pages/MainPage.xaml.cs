using Microsoft.Maui.Controls;
using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;
using RhythmVerseClient.Pages;

namespace RhythmVerseClient
{
    public partial class MainPage : TabbedPage
    {

        public MainPage(MainViewModel mainView, DownloadViewModel downView, CloneHeroViewModel cloneView)
        {
            InitializeComponent();
            BindingContext = mainView;

            var DownloadsPage = new DownloadPage(downView);
            Children.Add(DownloadsPage);
            var CloneHeroPage = new CloneHeroPage(cloneView);
            Children.Add(CloneHeroPage);
        }

        private void OnButtonClicked(object sender, EventArgs e)
        {
            // Handle button click event
            DisplayAlert("Alert", "Button was clicked!", "OK");
        }
    }
}
