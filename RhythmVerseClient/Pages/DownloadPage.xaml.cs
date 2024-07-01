using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Pages;

public partial class DownloadPage : ContentPage
{

    public ResourceWatcher DownloadWatcher { get; set; }

    public DownloadPage()
	{
		InitializeComponent();
		BindingContext = new DownloadViewModel(fileSystem);
        DownloadWatcher = fileSystem.GetDownloadWatcher();
        DownloadWatcher.LoadItems();
        DownloadList.SelectionChanged += DownloadList_SelectionChanged;
    }

    private void DownloadList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        //throw new NotImplementedException();

    }
}