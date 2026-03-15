using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RhythmVerseClient.ViewModels;
using RhythmVerseClient.Views;
using System;

namespace RhythmVerseClient;

public class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
            return new TextBlock { Text = "No View Model" };

        var name = data.GetType().FullName!;

        // Handle the naming convention: ViewModels -> Views, ViewModel -> View, Model -> View
        name = name.Replace("ViewModels", "Views");

        // Replace ViewModel with View only if it ends with ViewModel
        if (name.EndsWith("ViewModel"))
            name = name.Replace("ViewModel", "View");
        // Otherwise if it ends with Model, replace with View
        else if (name.EndsWith("Model"))
            name = name.Replace("Model", "View");

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
                return new TextBlock { Text = $"Error creating view: {ex.Message}" };
            }
        }

        return new TextBlock { Text = $"View not found: {name}" };
    }

    public bool Match(object? data)
    {
        return data is RhythmVerseViewModel
            or MainViewModel
            or DownloadViewModel
            or CloneHeroViewModel
            or InstallSongViewModel
            or AppShellViewModel
            or AuthGateViewModel;
    }
}
