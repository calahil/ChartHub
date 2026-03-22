using ChartHub.Configuration.Metadata;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public sealed class SettingsFieldViewModelTests
{
    [Fact]
    public void EditorKindFlags_AreComputedFromEditorKind()
    {
        var directoryField = new SettingsFieldViewModel
        {
            EditorKind = SettingEditorKind.DirectoryPicker,
            Description = "Directory setting",
        };

        Assert.True(directoryField.IsDirectoryPicker);
        Assert.True(directoryField.IsPathPicker);
        Assert.False(directoryField.IsFilePicker);
        Assert.False(directoryField.IsTextEditor);
        Assert.True(directoryField.HasDescription);
        Assert.Equal("Browse Folder", directoryField.BrowseButtonText);

        var fileField = new SettingsFieldViewModel
        {
            EditorKind = SettingEditorKind.FilePicker,
        };

        Assert.True(fileField.IsFilePicker);
        Assert.True(fileField.IsPathPicker);
        Assert.Equal("Browse File", fileField.BrowseButtonText);
    }

    [Fact]
    public void Setters_UpdateState_AndErrorFlag()
    {
        var field = new SettingsFieldViewModel
        {
            EditorKind = SettingEditorKind.Number,
        };

        field.StringValue = "abc";
        field.BoolValue = true;
        field.NumberValue = 42;
        field.SelectedOption = "OptionA";
        field.IsGroupHeaderVisible = true;

        Assert.Equal("abc", field.StringValue);
        Assert.True(field.BoolValue);
        Assert.Equal(42, field.NumberValue);
        Assert.Equal("OptionA", field.SelectedOption);
        Assert.True(field.IsGroupHeaderVisible);

        field.ErrorMessage = "Invalid value";
        Assert.True(field.HasError);

        field.ErrorMessage = string.Empty;
        Assert.False(field.HasError);
    }

    [Fact]
    public void SecretFieldViewModel_Setters_UpdateDerivedState()
    {
        var secret = new SecretFieldViewModel
        {
            Label = "Secret",
            Key = "demo",
        };

        Assert.False(secret.HasDraftValue);
        Assert.Equal("Not set", secret.StorageStatus);

        secret.Value = "draft";
        secret.HasStoredValue = true;
        secret.IsBusy = true;

        Assert.True(secret.HasDraftValue);
        Assert.Equal("Stored", secret.StorageStatus);
        Assert.True(secret.IsBusy);
    }
}
