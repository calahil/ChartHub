using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Pages
{
    public partial class CloneHeroPage : ContentPage
    {
        private CloneHeroViewModel viewModel;

        public CloneHeroPage(CloneHeroViewModel cloneHeroView)
        {
            viewModel = cloneHeroView;
            InitializeComponent();
            BindingContext = cloneHeroView;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            viewModel.CloneHeroWatcher.LoadItems();
        }
    }
}