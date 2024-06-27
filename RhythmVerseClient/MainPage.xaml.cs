using Microsoft.Maui.Controls;
using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient
{
    public partial class MainPage : TabbedPage
    {

        public MainPage(MainViewModel mainView)
        {
            InitializeComponent();
            BindingContext = mainView;


            DownloadList.SelectionChanged += DownloadList_SelectionChanged;
            //DownloadPage.BindingContext = mainView.DownloadWatcher;
            //CloneHeroPage.BindingContext = mainView.CloneHeroSongsWatcher;
            //DownloadList.ItemsSource = mainView.DownloadWatcher.Data;
            //CloneHeroList.ItemsSource = mainView.CloneHeroSongsWatcher.Data;
        }

        private void DownloadList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void OnButtonClicked(object sender, EventArgs e)
        {
            // Handle button click event
            DisplayAlert("Alert", "Button was clicked!", "OK");
        }
    }
}
