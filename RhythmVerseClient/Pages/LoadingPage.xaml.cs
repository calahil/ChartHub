namespace RhythmVerseClient.Pages;

public partial class LoadingPage : ContentPage
{
    public LoadingPage()
    {
        InitializeComponent();

        Content = new Grid
        {
            VerticalOptions = LayoutOptions.CenterAndExpand,
            HorizontalOptions = LayoutOptions.CenterAndExpand,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            },
            Children =
            {
                new ActivityIndicator
                {
                    IsRunning = true,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = "Initializing...",
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center
                }
            }
        };

        Grid.SetRow(new Label { Text = "Initializing..." }, 0);
        Grid.SetRow(new ActivityIndicator { IsRunning = true }, 1);
    }
}