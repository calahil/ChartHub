using System;

using Avalonia.Controls;
using Avalonia.Controls.Templates;

using ChartHub.Utilities;
using ChartHub.ViewModels;
using ChartHub.Views;

namespace ChartHub;

public class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
        {
            return new TextBlock { Text = "No View Model" };
        }

        string name = data.GetType().FullName!;

        // Handle the naming convention: ViewModels -> Views, ViewModel -> View, Model -> View
        name = name.Replace("ViewModels", "Views");

        // Replace ViewModel with View only if it ends with ViewModel
        if (name.EndsWith("ViewModel"))
        {
            name = name.Replace("ViewModel", "View");
        }
        // Otherwise if it ends with Model, replace with View
        else if (name.EndsWith("Model"))
        {
            name = name.Replace("Model", "View");
        }

        var type = Type.GetType(name);

        if (type != null)
        {
            try
            {
                var control = (Control)Activator.CreateInstance(type)!;
                control.DataContext = data;
                return control;
            }
            catch (Exception ex)
            {
                Logger.LogError("UI", "Failed to create view for view model", ex, new Dictionary<string, object?>
                {
                    ["viewName"] = name,
                    ["viewModelType"] = data.GetType().FullName,
                });
                return new TextBlock { Text = $"Error creating view: {ex.Message}" };
            }
        }

        return new TextBlock { Text = $"View not found: {name}" };
    }

    public bool Match(object? data)
    {
        return data is RhythmVerseViewModel
            or EncoreViewModel
            or MainViewModel
            or DownloadViewModel
            or CloneHeroViewModel
            or DesktopEntryViewModel
            or VolumeViewModel
            or SettingsViewModel
            or AppShellViewModel
            or SplashViewModel
            or InputShellViewModel
            or VirtualControllerViewModel
            or VirtualTouchPadViewModel
            or VirtualKeyboardViewModel;
    }
}
