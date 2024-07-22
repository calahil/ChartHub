using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Pages;

public partial class RhythmVersePage : ContentPage
{
	private RhythmVerseModel viewModel;

	public RhythmVersePage(RhythmVerseModel verseModel)
	{
		InitializeComponent();
		viewModel = verseModel;
		BindingContext = viewModel;
	}

    protected async override void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadDataAsync();
    }

    private async void SongList_RemainingItemsThresholdReached(object sender, EventArgs e)
    {
		await viewModel.LoadDataAsync();
    }
}