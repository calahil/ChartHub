using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Pages
{
    public partial class InstallSongPage : ContentPage
    {
        private InstallSongViewModel viewModel;

        public InstallSongPage(InstallSongViewModel installSongView)
        {
            InitializeComponent();
            viewModel = installSongView;
            BindingContext = viewModel;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            viewModel.PhaseshiftWatcher.LoadItems();
        }
    }
}