using Microsoft.Maui.Controls;
using RhythmVerseClient.Services;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient
{
    public partial class MainPage : TabbedPage
    {

        public MainPage(MainViewModel mainView)
        {
            InitializeComponent();
            BindingContext = mainView;
        }

        private void OnButtonClicked(object sender, EventArgs e)
        {
            // Handle button click event
            DisplayAlert("Alert", "Button was clicked!", "OK");
        }
    }
}
