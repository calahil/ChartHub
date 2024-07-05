using RhythmVerseClient.Services;
using RhythmVerseClient.Utilities;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace RhythmVerseClient
{
    public partial class App : Application
    {
        private AppGlobalSettings _globalSettings;
       
        public App(IServiceProvider serviceProvider, AppGlobalSettings settings)
        {
            InitializeComponent();

            Current.UserAppTheme = AppTheme.Dark;
            _globalSettings = settings;
            Initialize();
            MainPage = serviceProvider.GetRequiredService<MainPage>();
        }
    }
}
