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

        SortPicker.ItemDisplayBinding
        SortPicker.SelectedIndexChanged += SortPicker_SelectedIndexChanged;
        OrderPicker.SelectedIndexChanged += OrderPicker_SelectedIndexChanged;
    }

    private void OrderPicker_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var picker = sender as Picker;
        if (picker != null)
        {
            viewModel.Filters.SelectedOrder = viewModel.Filters.SelectedFilter.Orders[picker.SelectedIndex].ToString();
        }
    }

    private void SortPicker_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var picker = sender as Picker;
        if (picker != null)
        {
            viewModel.Filters.SelectedFilter = viewModel.Filters.Filters[picker.SelectedIndex];
        }
    }

    protected async override void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadDataAsync();
    }
}
