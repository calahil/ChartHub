using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Pages;

public partial class DownloadPage : ContentPage
{
    private DownloadViewModel viewModel;

    public DownloadPage(DownloadViewModel downloadView)
	{
        viewModel = downloadView;
		InitializeComponent();
		BindingContext = downloadView;
    }

    private void DownloadList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        //throw new NotImplementedException();

    }

    private void OnCheckBoxCheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        viewModel.IsAnyChecked = viewModel.AnyItemChecked();
    }
}