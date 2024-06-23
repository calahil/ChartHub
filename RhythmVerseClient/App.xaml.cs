using RhythmVerseClient.Services;

namespace RhythmVerseClient
{
    public partial class App : Application
    {
        public App(IServiceProvider serviceProvider)
        {
            InitializeComponent();
            MainPage = serviceProvider.GetRequiredService<MainPage>();
        }
    }
}
