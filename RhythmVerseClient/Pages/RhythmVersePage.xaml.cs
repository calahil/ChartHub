using RhythmVerseClient.Models;
using RhythmVerseClient.ViewModels;
using WinRT;

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
        var parent = button.Parent;
        viewModel.SelectedFile = button.Parent.BindingContext as ViewSong;

        await viewModel.DownloadFile();
    }

    private void InstrumentPicker_SelectedIndexChanged(object sender, EventArgs e)
    {
        var picker = sender as Picker;
        if (picker != null && picker.SelectedIndex != -1)
        {
            var instrument = picker.ItemsSource[picker.SelectedIndex];
            var none = picker.ItemsSource[0];

            if (instrument != null)
            {
                if (!viewModel.SelectedInstruments.Contains(instrument))
                {
                    viewModel.SelectedInstruments.Add(instrument as InstrumentItem);

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
}
