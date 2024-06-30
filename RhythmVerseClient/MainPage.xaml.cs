using Microsoft.Maui.Controls;
using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient
{
    public partial class MainPage : TabbedPage
    {
        public IFileSystemManager FileManager { get; }
        public ResourceWatcher DownloadWatcher { get; set; }
        public ResourceWatcher CloneHeroSongsWatcher { get; set; }

        public MainPage(IFileSystemManager fileSystemManager, MainViewModel mainView, DownloadViewModel downloadViewModel)
        {
            InitializeComponent();
            BindingContext = mainView;
            DownloadPage.BindingContext = downloadViewModel;
            ContextMenuFlyOut.BindingContext = downloadViewModel;
            ContextMenuFlyOut.
            FileManager = fileSystemManager;
            FileManager.Initialize();

            DownloadWatcher = FileManager.GetDownloadWatcher();
            CloneHeroSongsWatcher = FileManager.GetCloneHeroSongWatcher();
            DownloadWatcher.LoadItems();
            CloneHeroSongsWatcher.LoadItems();
            downloadViewModel.DataItems = DownloadWatcher.Data;

            DownloadList.SelectionChanged += DownloadList_SelectionChanged;
            CloneHeroList.SelectionChanged += DownloadList_SelectionChanged;
            //DownloadPage.BindingContext = mainView.DownloadWatcher;
            //CloneHeroPage.BindingContext = mainView.CloneHeroSongsWatcher;
            //DownloadList.ItemsSource = mainView.DownloadWatcher.Data;
            //CloneHeroList.ItemsSource = mainView.CloneHeroSongsWatcher.Data;
        }

        private void DownloadList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            //throw new NotImplementedException();
            
        }

        private void OnButtonClicked(object sender, EventArgs e)
        {
            // Handle button click event
            DisplayAlert("Alert", "Button was clicked!", "OK");
        }
    }
}
