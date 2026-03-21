namespace ChartHub.Configuration.Metadata;

[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingGroupAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingDisplayAttribute(string label) : Attribute
{
    public string Label { get; } = label;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingDescriptionAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingEditorAttribute(SettingEditorKind editorKind) : Attribute
{
    public SettingEditorKind EditorKind { get; } = editorKind;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingHotReloadableAttribute(bool isHotReloadable) : Attribute
{
    public bool IsHotReloadable { get; } = isHotReloadable;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingRequiresRestartAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingHiddenAttribute : Attribute
{
}

public enum SettingEditorKind
{
    Text,
    Toggle,
    Number,
    Dropdown,
    DirectoryPicker,
    FilePicker,
}
