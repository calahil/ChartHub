using RhythmVerseClient.Platforms.Windows;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Pages
{
    public partial class InstallSongPage : ContentPage
    {
        private InstallSongViewModel viewModel;
        private IWindowSizeService _windowSizeService;

        public InstallSongPage(InstallSongViewModel installSongView, IWindowSizeService windowSizeService)
        {
            InitializeComponent();
            viewModel = installSongView;
            BindingContext = viewModel;
            _windowSizeService = windowSizeService;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            viewModel.PhaseshiftWatcher.LoadItems();
        }
    }
}