using Microsoft.Maui.Controls;
using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient
{
    public partial class MainPage : TabbedPage
    {
        public IFileSystemManager FileManager { get; }
        public ResourceWatcher CloneHeroSongsWatcher { get; set; }

        public MainPage(IFileSystemManager fileSystemManager, MainViewModel mainView)
        {
            InitializeComponent();
            BindingContext = mainView;

            //ContextMenuFlyOut.BindingContext = downloadViewModel;
            
            FileManager = fileSystemManager;
            FileManager.Initialize();

            CloneHeroSongsWatcher = FileManager.GetCloneHeroSongWatcher();
            CloneHeroSongsWatcher.LoadItems();
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
