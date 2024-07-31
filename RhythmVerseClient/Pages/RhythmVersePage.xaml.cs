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
        viewModel.IsPlaceholder = true;
        await viewModel.LoadDataAsync();
    }

    private async void Stepper_ValueChanged(object sender, ValueChangedEventArgs e)
    {
        viewModel.IsPlaceholder = true;
        await viewModel.LoadDataAsync();
    }

    private async void ImageButton_Clicked(object sender, EventArgs e)
    {
        var button = (Button)sender;
        var parent = button.Parent;
        viewModel.SelectedFile = button.Parent.BindingContext as ViewSong;

        await viewModel.DownloadFile();
    }
}
