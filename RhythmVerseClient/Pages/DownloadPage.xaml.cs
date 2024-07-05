using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Pages
{

    public partial class DownloadPage : ContentPage
    {
        private DownloadViewModel viewModel;

        public DownloadPage(DownloadViewModel downloadView, InstallSongViewModel installSongView)
        {
            viewModel = downloadView;
            InitializeComponent();
            BindingContext = downloadView;
            viewModel.InstallItems = installSongView.InstallSongs = [];
        }

        private void DownloadList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            //throw new NotImplementedException();

        }

        private void CheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            viewModel.IsAnyChecked = viewModel.AnyItemChecked();
        }
    }
}