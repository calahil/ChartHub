using RhythmVerseClient.Pages;
using RhythmVerseClient.Utilities;

namespace RhythmVerseClient
{
    public partial class App : Application
    {
        private AppGlobalSettings _globalSettings;
        private IServiceProvider _serviceProvider;

        public App(IServiceProvider serviceProvider, AppGlobalSettings settings, Initializer initializer)
        {
            InitializeComponent();
            if (Current != null)
            {
                Current.UserAppTheme = AppTheme.Dark;
            }
            _globalSettings = settings;
            MainPage = new LoadingPage();
            _serviceProvider = serviceProvider;

            InitializeAsync(initializer);

        }

        private async void InitializeAsync(Initializer initializer)
        {
            await Initializer.InitializeAsync();
            MainPage = _serviceProvider.GetRequiredService<MainPage>();
        }
    }
}
