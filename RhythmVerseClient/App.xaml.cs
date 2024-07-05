using Microsoft.Extensions.DependencyInjection;
using RhythmVerseClient.Services;
using RhythmVerseClient.Utilities;
using RhythmVerseClient.Pages;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;

namespace RhythmVerseClient
{
    public partial class App : Application
    {
        private AppGlobalSettings _globalSettings;
        private IServiceProvider _serviceProvider;

        public App(IServiceProvider serviceProvider, AppGlobalSettings settings, Initializer initializer)
        {
            InitializeComponent();

            Current.UserAppTheme = AppTheme.Dark;
            _globalSettings = settings;
            MainPage = new LoadingPage();
            _serviceProvider = serviceProvider;

            InitializeAsync(initializer);

        }

        private async void InitializeAsync(Initializer initializer)
        {
            await initializer.InitializeAsync();
            MainPage = _serviceProvider.GetRequiredService<MainPage>();
        }
    }
}
