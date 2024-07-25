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

    private void SongList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (viewModel.SelectedFile == null)
            return;
        var popup = new DownloadPopup(viewModel.SelectedFile.File.DownloadPageUrlFull.OriginalString);
        this.ShowPopup(popup);
        //await fileDownloadService.DownloadFileAsync(SelectedFile, globalSettings.StagingDir);
    }
}