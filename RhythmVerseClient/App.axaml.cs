using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using RhythmVerseClient.Utilities;
using System.Reflection;

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
            // Dynamically set MainWindow since the interface may not be directly available
            if (ApplicationLifetime != null)
            {
                PropertyInfo? mainWindowProp = ApplicationLifetime.GetType().GetProperty("MainWindow");
                if (mainWindowProp != null)
                {
                    mainWindowProp.SetValue(ApplicationLifetime, new MainWindow());
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
