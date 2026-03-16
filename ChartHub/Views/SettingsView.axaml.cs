using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ChartHub.Configuration.Metadata;
using ChartHub.Utilities;
using ChartHub.ViewModels;

namespace ChartHub.Views;

public partial class SettingsView : Avalonia.Controls.UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnBrowsePathClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SettingsFieldViewModel field })
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider is null)
            return;

        try
        {
            if (field.EditorKind == SettingEditorKind.DirectoryPicker)
            {
                var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    AllowMultiple = false,
                    Title = $"Select folder for {field.Label}",
                });

                var selected = folders.FirstOrDefault();
                if (selected is not null)
                    field.StringValue = ToDisplayPath(selected.Path);

                return;
            }

            if (field.EditorKind == SettingEditorKind.FilePicker)
            {
                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    Title = $"Select file for {field.Label}",
                });

                var selected = files.FirstOrDefault();
                if (selected is not null)
                    field.StringValue = ToDisplayPath(selected.Path);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("UI", "Settings path picker failed", ex, new Dictionary<string, object?>
            {
                ["fieldKey"] = field.Key,
                ["label"] = field.Label,
                ["editorKind"] = field.EditorKind.ToString(),
            });
            if (DataContext is SettingsViewModel vm)
                vm.StatusMessage = $"Path picker failed: {ex.Message}";
        }
    }

    private static string ToDisplayPath(Uri? uri)
    {
        if (uri is null)
            return string.Empty;

        if (uri.IsFile)
            return uri.LocalPath;

        return uri.ToString();
    }
}
