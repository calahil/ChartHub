using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Pages
{

    public partial class DownloadPage : ContentPage
    {
        private DownloadViewModel viewModel;

        public DownloadPage(DownloadViewModel downloadView)
        {
            viewModel = downloadView;
            InitializeComponent();
            BindingContext = downloadView;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            viewModel.DownloadWatcher.LoadItems();
        }
      
        private void CheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e)
        {
            viewModel.IsAnyChecked = viewModel.AnyItemChecked();
        }

        private async void Button_Clicked(object sender, EventArgs e)
        {
            await Task.Delay(1000);
        }
    }
}