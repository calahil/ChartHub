using RhythmVerseClient.Utilities;
using RhythmVerseClient.ViewModels;
using Syncfusion.Maui.Data;

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

            CloneHeroList.Columns[0].HeaderText = viewModel.PageStrings.Select;
            CloneHeroList.Columns[1].HeaderText = viewModel.PageStrings.DisplayName;
            CloneHeroList.Columns[2].HeaderText = viewModel.PageStrings.FileType;
            CloneHeroList.Columns[3].HeaderText = viewModel.PageStrings.FileSize;

            CloneHeroList.SortComparers.Add(new SortComparer()
            {
                PropertyName = "FileSize",
                Comparer = new Toolbox.FileSizeComparer()
            });

            CloneHeroList.SortComparers.Add(new SortComparer()
            {
                PropertyName = "ImageFile",
                Comparer = new Toolbox.FileTypeComparer()
            });
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            viewModel.CloneHeroWatcher.LoadItems();
        }
    }
}