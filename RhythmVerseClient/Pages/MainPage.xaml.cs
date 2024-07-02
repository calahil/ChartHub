using Microsoft.Maui.Controls;
using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;
using RhythmVerseClient.Pages;

namespace RhythmVerseClient
{
    public partial class MainPage : TabbedPage
    {
        public ResourceWatcher CloneHeroSongsWatcher { get; set; }

        public MainPage(MainViewModel mainView, DownloadViewModel downView)
        {
            InitializeComponent();
            BindingContext = mainView;

            var DownloadsPage = new DownloadPage(downView);
            Children.Add(DownloadsPage);

            //ContextMenuFlyOut.BindingContext = downloadViewModel;
        /*    
            FileManager = fileSystemManager;
            FileManager.Initialize();

            CloneHeroSongsWatcher = FileManager.GetCloneHeroSongWatcher();
            CloneHeroSongsWatcher.LoadItems();*/
           // ContextMenuFlyOut.GenerateContextMenuItems();
           //DownloadPage.BindingContext = mainView.DownloadWatcher;
            //CloneHeroPage.BindingContext = mainView.CloneHeroSongsWatcher;
            //DownloadList.ItemsSource = mainView.DownloadWatcher.Data;
            //CloneHeroList.ItemsSource = mainView.CloneHeroSongsWatcher.Data;
        }

        private void OnButtonClicked(object sender, EventArgs e)
        {
            // Handle button click event
            DisplayAlert("Alert", "Button was clicked!", "OK");
        }
    }
}
