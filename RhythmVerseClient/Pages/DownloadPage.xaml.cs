using RhythmVerseClient.ViewModels;
using RhythmVerseClient.Utilities;
using Syncfusion.Maui.Data;

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

            DownloadList.Columns[0].HeaderText = viewModel.PageStrings.Install;
            DownloadList.Columns[1].HeaderText = viewModel.PageStrings.DisplayName;
            DownloadList.Columns[2].HeaderText = viewModel.PageStrings.FileType;
            DownloadList.Columns[3].HeaderText = viewModel.PageStrings.FileSize;

            DownloadList.SortComparers.Add(new SortComparer()
            {
                PropertyName = "FileSize",
                Comparer = new Toolbox.FileSizeComparer()
            });

            DownloadList.SortComparers.Add(new SortComparer()
            {
                PropertyName = "ImageFile",
                Comparer = new Toolbox.FileTypeComparer()
            });
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