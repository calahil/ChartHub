using RhythmVerseClient.Models;
using RhythmVerseClient.ViewModels;
using WinRT;

namespace RhythmVerseClient.Pages;

public partial class RhythmVersePage : ContentPage
{
    private readonly RhythmVerseModel viewModel;

    public RhythmVersePage(RhythmVerseModel verseModel)
    {
        InitializeComponent();
        viewModel = verseModel;
        BindingContext = viewModel;

    }

    protected async override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.IsPlaceholder = true;
        await viewModel.LoadDataAsync(false);
    }

    private async void Stepper_ValueChanged(object sender, ValueChangedEventArgs e)
    {
        viewModel.IsPlaceholder = true;
        await viewModel.LoadDataAsync(true);
    }

    private async void ImageButton_Clicked(object sender, EventArgs e)
    {
        var button = (Button)sender;
        viewModel.SelectedFile = button.Parent.BindingContext as ViewSong;

        await viewModel.DownloadFile();
    }

    private void InstrumentPicker_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (sender is Picker picker && picker.SelectedIndex != -1)
        {
            if (picker.ItemsSource[picker.SelectedIndex] is InstrumentItem instrument && picker.ItemsSource[0] is InstrumentItem none)
            {

                if (!viewModel.SelectedInstruments.Contains(instrument))
                {
                    viewModel.SelectedInstruments.Add(instrument);

                    if (viewModel.SelectedInstruments.Contains(none))
                        viewModel.SelectedInstruments.Remove(none as InstrumentItem);
                }
                else
                {
                    viewModel.SelectedInstruments.Remove(instrument as InstrumentItem);
                    if (!viewModel.SelectedInstruments.Any())
                    {
                        viewModel.SelectedInstruments.Add(none as InstrumentItem);
                    }
                }
            }

            picker.SelectedItem = null;
            picker.SelectedIndex = -1;
        }
    }

    private void Button_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button)
        {
            var song = (ViewSong)button.BindingContext;
            viewModel.SearchAuthorText = song.Author.Name;
        }
        viewModel.RefreshButton();
    }
}
