using CommunityToolkit.Maui.Views;
using RhythmVerseClient.Api;
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

    private void Picker_SelectedIndexChanged(object sender, EventArgs e)
    {
        viewModel.SortDataItems();
    }
}