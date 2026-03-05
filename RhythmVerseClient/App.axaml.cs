using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using RhythmVerseClient.Views;

namespace RhythmVerseClient
{
    public partial class App : Avalonia.Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Set the main window via reflection to avoid namespace dependencies
            var mainWindowProp = ApplicationLifetime?.GetType().GetProperty("MainWindow");
            if (mainWindowProp != null && mainWindowProp.CanWrite)
            {
                mainWindowProp.SetValue(ApplicationLifetime, new MainView());
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
