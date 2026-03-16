using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Metadata;
using ChartHub.Configuration.Models;
using ChartHub.Configuration.Secrets;
using ChartHub.Configuration.Stores;
using ChartHub.Utilities;

namespace ChartHub.ViewModels;

public sealed class SettingsFieldViewModel : INotifyPropertyChanged
{
    private string _stringValue = string.Empty;
    private bool _boolValue;
    private bool _isGroupHeaderVisible;
    private string _errorMessage = string.Empty;
    private double _numberValue;
    private string _selectedOption = string.Empty;

    public string Group { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public SettingEditorKind EditorKind { get; init; }
    public bool IsHotReloadable { get; init; }
    public bool RequiresRestart { get; init; }

    public object SectionRef { get; init; } = default!;
    public PropertyInfo Property { get; init; } = default!;

    public bool IsTextEditor => EditorKind == SettingEditorKind.Text;
    public bool IsToggleEditor => EditorKind == SettingEditorKind.Toggle;
    public bool IsNumberEditor => EditorKind == SettingEditorKind.Number;
    public bool IsDropdownEditor => EditorKind == SettingEditorKind.Dropdown;
    public bool IsDirectoryPicker => EditorKind == SettingEditorKind.DirectoryPicker;
    public bool IsFilePicker => EditorKind == SettingEditorKind.FilePicker;
    public bool IsPathPicker => IsDirectoryPicker || IsFilePicker;
    public string BrowseButtonText => IsDirectoryPicker ? "Browse Folder" : "Browse File";
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();

    public string StringValue
    {
        get => _stringValue;
        set
        {
            if (_stringValue == value)
                return;

            _stringValue = value;
            OnPropertyChanged();
        }
    }

    public bool BoolValue
    {
        get => _boolValue;
        set
        {
            if (_boolValue == value)
                return;

            _boolValue = value;
            OnPropertyChanged();
        }
    }

    public double NumberValue
    {
        get => _numberValue;
        set
        {
            if (Math.Abs(_numberValue - value) < double.Epsilon)
                return;

            _numberValue = value;
            OnPropertyChanged();
        }
    }

    public string SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (_selectedOption == value)
                return;

            _selectedOption = value;
            OnPropertyChanged();
        }
    }

    public bool IsGroupHeaderVisible
    {
        get => _isGroupHeaderVisible;
        set
        {
            if (_isGroupHeaderVisible == value)
                return;

            _isGroupHeaderVisible = value;
            OnPropertyChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (_errorMessage == value)
                return;

            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SecretFieldViewModel : INotifyPropertyChanged
{
    private string _value = string.Empty;
    private bool _hasStoredValue;
    private bool _isBusy;

    public string Label { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;

            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDraftValue));
        }
    }

    public bool HasStoredValue
    {
        get => _hasStoredValue;
        set
        {
            if (_hasStoredValue == value)
                return;

            _hasStoredValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StorageStatus));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value)
                return;

            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public bool HasDraftValue => !string.IsNullOrWhiteSpace(Value);

    public string StorageStatus => HasStoredValue ? "Stored" : "Not set";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISettingsOrchestrator _settings;
    private readonly ISecretStore _secretStore;

    private string _statusMessage = "";
    private bool _hasPendingRestartSettings;
    private bool _isSaving;
    private bool _showDeveloperSettings;

    public ObservableCollection<SettingsFieldViewModel> Fields { get; } = [];
    public ObservableCollection<SecretFieldViewModel> Secrets { get; } = [];

    public AsyncRelayCommand SaveCommand { get; }
    public IRelayCommand ReloadCommand { get; }
    public IAsyncRelayCommand<SecretFieldViewModel?> SaveSecretCommand { get; }
    public IAsyncRelayCommand<SecretFieldViewModel?> ClearSecretCommand { get; }
    public string SecretStorageBackend { get; }

#if DEBUG
    public bool IsDeveloperBuild => true;
#else
    public bool IsDeveloperBuild => false;
#endif

    public bool ShowDeveloperSettings
    {
        get => _showDeveloperSettings;
        set
        {
            if (_showDeveloperSettings == value)
                return;

            _showDeveloperSettings = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSecretsPanelVisible));
        }
    }

    public bool IsSecretsPanelVisible => IsDeveloperBuild && ShowDeveloperSettings;

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage == value)
                return;

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public bool HasPendingRestartSettings
    {
        get => _hasPendingRestartSettings;
        set
        {
            if (_hasPendingRestartSettings == value)
                return;

            _hasPendingRestartSettings = value;
            OnPropertyChanged();
        }
    }

    public bool HasValidationErrors => Fields.Any(f => f.HasError);

    public IReadOnlyList<string> ValidationIssueSummaries => Fields
        .Where(f => f.HasError)
        .GroupBy(f => string.IsNullOrWhiteSpace(f.Group) ? "General" : f.Group)
        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .Select(g => $"{g.Key}: {g.Count()} issue{(g.Count() == 1 ? string.Empty : "s")}")
        .ToList();

    public SettingsViewModel(ISettingsOrchestrator settings, ISecretStore secretStore)
    {
        _settings = settings;
        _secretStore = secretStore;
        _showDeveloperSettings = false;

        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        ReloadCommand = new RelayCommand(Reload);
        SaveSecretCommand = new AsyncRelayCommand<SecretFieldViewModel?>(SaveSecretAsync);
        ClearSecretCommand = new AsyncRelayCommand<SecretFieldViewModel?>(ClearSecretAsync);
        SecretStorageBackend = ResolveSecretStorageBackend(secretStore);

        RebuildFieldsFrom(_settings.Current);
        if (IsDeveloperBuild)
            BuildSecrets();
        _settings.SettingsChanged += OnSettingsChanged;
        if (IsDeveloperBuild)
            _ = RefreshSecretStateAsync();
    }

    private static string ResolveSecretStorageBackend(ISecretStore secretStore)
    {
        if (secretStore is DesktopSecretStore desktopSecretStore)
            return $"Desktop ({desktopSecretStore.BackendName})";

        if (OperatingSystem.IsAndroid())
            return "Android (Keystore)";

        return secretStore.GetType().Name;
    }

    private void OnSettingsChanged(AppConfigRoot config)
    {
        RebuildFieldsFrom(config);
    }

    private void RebuildFieldsFrom(AppConfigRoot config)
    {
        foreach (var field in Fields)
            field.PropertyChanged -= OnFieldPropertyChanged;

        var fieldBuffer = new List<SettingsFieldViewModel>();
        AddSectionFields(fieldBuffer, config.Runtime, "Runtime");
        AddSectionFields(fieldBuffer, config.GoogleAuth, "GoogleAuth");

        fieldBuffer = fieldBuffer
            .OrderBy(f => f.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string previousGroup = string.Empty;
        foreach (var field in fieldBuffer)
        {
            field.IsGroupHeaderVisible = !string.Equals(previousGroup, field.Group, StringComparison.OrdinalIgnoreCase);
            previousGroup = field.Group;
        }

        Fields.Clear();
        foreach (var field in fieldBuffer)
        {
            field.PropertyChanged += OnFieldPropertyChanged;
            Fields.Add(field);
        }

        HasPendingRestartSettings = Fields.Any(f => f.RequiresRestart);
        RefreshSaveState();
    }

    private void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SettingsFieldViewModel field)
            return;

        if (e.PropertyName is not nameof(SettingsFieldViewModel.StringValue)
            and not nameof(SettingsFieldViewModel.BoolValue)
            and not nameof(SettingsFieldViewModel.NumberValue)
            and not nameof(SettingsFieldViewModel.SelectedOption))
            return;

        RevalidateFieldLive(field);
        RefreshSaveState();
    }

    private static void RevalidateFieldLive(SettingsFieldViewModel field)
    {
        if (!field.IsPathPicker)
        {
            if (field.HasError)
                field.ErrorMessage = string.Empty;
            return;
        }

        field.ErrorMessage = ValidateDraftField(field) ?? string.Empty;
    }

    private void BuildSecrets()
    {
        Secrets.Clear();
        Secrets.Add(new SecretFieldViewModel
        {
            Label = "Google Desktop Client Secret",
            Key = SecretKeys.GoogleDesktopClientSecret,
            Description = "Sensitive OAuth client secret used for desktop auth flow.",
        });
        Secrets.Add(new SecretFieldViewModel
        {
            Label = "Google Refresh Token",
            Key = SecretKeys.GoogleRefreshToken,
            Description = "OAuth refresh token used to obtain new access tokens.",
        });
        Secrets.Add(new SecretFieldViewModel
        {
            Label = "Google Access Token",
            Key = SecretKeys.GoogleAccessToken,
            Description = "Current short-lived access token. Usually set automatically.",
        });
    }

    private async Task RefreshSecretStateAsync()
    {
        foreach (var secret in Secrets)
        {
            try
            {
                secret.HasStoredValue = await _secretStore.ContainsAsync(secret.Key);
            }
            catch (Exception ex)
            {
                Logger.LogError("Secrets", "Failed to inspect secret storage state", ex, new Dictionary<string, object?>
                {
                    ["secretKey"] = secret.Key,
                });
            }
        }
    }

    private static void AddSectionFields(List<SettingsFieldViewModel> destination, object section, string sectionName)
    {
        var properties = section.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.CanWrite);

        foreach (var property in properties)
        {
            var display = property.GetCustomAttribute<SettingDisplayAttribute>()?.Label ?? property.Name;
            var group = property.GetCustomAttribute<SettingGroupAttribute>()?.Name ?? "General";
            var description = property.GetCustomAttribute<SettingDescriptionAttribute>()?.Text ?? string.Empty;
            var editorKind = ResolveEditorKind(property);
            var isHotReloadable = property.GetCustomAttribute<SettingHotReloadableAttribute>()?.IsHotReloadable ?? false;
            var requiresRestart = property.GetCustomAttribute<SettingRequiresRestartAttribute>() is not null;

            var field = new SettingsFieldViewModel
            {
                Group = group,
                Label = display,
                Description = description,
                Key = $"{sectionName}.{property.Name}",
                EditorKind = editorKind,
                IsHotReloadable = isHotReloadable,
                RequiresRestart = requiresRestart,
                SectionRef = section,
                Property = property,
                Options = ResolveOptions(property, editorKind),
            };

            var value = property.GetValue(section);
            if (property.PropertyType == typeof(bool))
                field.BoolValue = value is bool b && b;
            else if (field.EditorKind == SettingEditorKind.Number)
                field.NumberValue = ToDouble(value);
            else if (field.EditorKind == SettingEditorKind.Dropdown)
                field.SelectedOption = value?.ToString() ?? field.Options.FirstOrDefault() ?? string.Empty;
            else
                field.StringValue = value?.ToString() ?? string.Empty;

            destination.Add(field);
        }
    }

    private static SettingEditorKind ResolveEditorKind(PropertyInfo property)
    {
        var explicitEditor = property.GetCustomAttribute<SettingEditorAttribute>()?.EditorKind;
        if (explicitEditor.HasValue)
            return explicitEditor.Value;

        if (property.PropertyType.IsEnum)
            return SettingEditorKind.Dropdown;

        if (IsNumericType(property.PropertyType))
            return SettingEditorKind.Number;

        return property.PropertyType == typeof(bool)
            ? SettingEditorKind.Toggle
            : SettingEditorKind.Text;
    }

    private static IReadOnlyList<string> ResolveOptions(PropertyInfo property, SettingEditorKind editorKind)
    {
        if (editorKind == SettingEditorKind.Dropdown && property.PropertyType.IsEnum)
            return Enum.GetNames(property.PropertyType);

        return Array.Empty<string>();
    }

    private static bool IsNumericType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(byte)
            || underlying == typeof(sbyte)
            || underlying == typeof(short)
            || underlying == typeof(ushort)
            || underlying == typeof(int)
            || underlying == typeof(uint)
            || underlying == typeof(long)
            || underlying == typeof(ulong)
            || underlying == typeof(float)
            || underlying == typeof(double)
            || underlying == typeof(decimal);
    }

    private static double ToDouble(object? value)
    {
        if (value is null)
            return 0;

        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private async Task SaveAsync()
    {
        if (!CanSave())
            return;

        var saveStopwatch = Stopwatch.StartNew();
        Logger.LogInfo("Config", "Settings save started", new Dictionary<string, object?>
        {
            ["fieldCount"] = Fields.Count,
        });

        _isSaving = true;
        RefreshSaveState();
        try
        {
            foreach (var field in Fields)
                field.ErrorMessage = string.Empty;

            var hasLocalValidationFailures = false;
            foreach (var field in Fields)
            {
                var validationError = ValidateDraftField(field);
                if (string.IsNullOrWhiteSpace(validationError))
                    continue;

                field.ErrorMessage = validationError;
                hasLocalValidationFailures = true;
            }

            if (hasLocalValidationFailures)
            {
                Logger.LogWarning("Config", "Settings validation failed before save", new Dictionary<string, object?>
                {
                    ["fieldKeys"] = string.Join(",", Fields.Where(f => f.HasError).Select(f => f.Key)),
                    ["elapsedMs"] = saveStopwatch.ElapsedMilliseconds,
                });
                StatusMessage = "Settings validation failed. Review highlighted fields.";
                return;
            }

            var result = await _settings.UpdateAsync(config =>
            {
                foreach (var field in Fields)
                {
                    ApplyField(config, field);
                }
            });

            if (!result.IsValid)
            {
                foreach (var failure in result.Failures)
                {
                    var field = Fields.FirstOrDefault(f => string.Equals(f.Key, failure.Key, StringComparison.Ordinal));
                    if (field is not null)
                        field.ErrorMessage = failure.Message;
                }

                Logger.LogWarning("Config", "Settings validation failed during save", new Dictionary<string, object?>
                {
                    ["fieldKeys"] = string.Join(",", result.Failures.Select(f => f.Key)),
                    ["elapsedMs"] = saveStopwatch.ElapsedMilliseconds,
                });
                StatusMessage = "Settings validation failed. Review highlighted fields.";
                return;
            }

            Logger.LogInfo("Config", "Settings save completed", new Dictionary<string, object?>
            {
                ["fieldCount"] = Fields.Count,
                ["elapsedMs"] = saveStopwatch.ElapsedMilliseconds,
            });
            StatusMessage = "Settings saved.";
        }
        catch (Exception ex)
        {
            Logger.LogError("Config", "Settings save failed unexpectedly", ex, new Dictionary<string, object?>
            {
                ["fieldCount"] = Fields.Count,
                ["elapsedMs"] = saveStopwatch.ElapsedMilliseconds,
            });
            StatusMessage = "Settings save failed. See logs for details.";
        }
        finally
        {
            _isSaving = false;
            RefreshSaveState();
        }
    }

    private bool CanSave()
    {
        return !_isSaving && !HasValidationErrors;
    }

    private void RefreshSaveState()
    {
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(ValidationIssueSummaries));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private static string? ValidateDraftField(SettingsFieldViewModel field)
    {
        if (!field.IsPathPicker)
            return null;

        var value = field.StringValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return "A path value is required.";

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile)
            return "Path must be a local filesystem path.";

        var path = value;
        if (field.IsDirectoryPicker)
            return ValidateDirectoryPath(path);

        if (field.IsFilePicker)
            return ValidateFilePath(path);

        return null;
    }

    private static string? ValidateDirectoryPath(string path)
    {
        if (Directory.Exists(path))
            return null;

        if (File.Exists(path))
            return "Expected a directory path, but the path points to a file.";

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            return null;

        return "Directory does not exist and parent folder is not available.";
    }

    private static string? ValidateFilePath(string path)
    {
        if (File.Exists(path))
            return null;

        if (Directory.Exists(path))
            return "Expected a file path, but the path points to a directory.";

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            return null;

        return "File does not exist and parent folder is not available.";
    }

    private static void ApplyField(AppConfigRoot config, SettingsFieldViewModel field)
    {
        var section = field.Key.StartsWith("Runtime.", StringComparison.Ordinal)
            ? (object)config.Runtime
            : config.GoogleAuth;

        if (field.Property.PropertyType == typeof(bool))
        {
            field.Property.SetValue(section, field.BoolValue);
            return;
        }

        if (field.Property.PropertyType == typeof(string))
        {
            field.Property.SetValue(section, field.StringValue);
            return;
        }

        if (field.EditorKind == SettingEditorKind.Number)
        {
            var targetType = Nullable.GetUnderlyingType(field.Property.PropertyType) ?? field.Property.PropertyType;
            var converted = Convert.ChangeType(field.NumberValue, targetType, CultureInfo.InvariantCulture);
            field.Property.SetValue(section, converted);
            return;
        }

        if (field.EditorKind == SettingEditorKind.Dropdown && field.Property.PropertyType.IsEnum)
        {
            var option = string.IsNullOrWhiteSpace(field.SelectedOption)
                ? field.Options.FirstOrDefault() ?? string.Empty
                : field.SelectedOption;
            var parsed = Enum.Parse(field.Property.PropertyType, option, ignoreCase: true);
            field.Property.SetValue(section, parsed);
        }
    }

    private void Reload()
    {
        var reloadStopwatch = Stopwatch.StartNew();
        Logger.LogInfo("Config", "Settings reload started");

        try
        {
            RebuildFieldsFrom(_settings.Current);
            _ = RefreshSecretStateAsync();
            Logger.LogInfo("Config", "Settings reload completed", new Dictionary<string, object?>
            {
                ["fieldCount"] = Fields.Count,
                ["elapsedMs"] = reloadStopwatch.ElapsedMilliseconds,
            });
            StatusMessage = "Settings reloaded from current configuration.";
        }
        catch (Exception ex)
        {
            Logger.LogError("Config", "Settings reload failed", ex, new Dictionary<string, object?>
            {
                ["elapsedMs"] = reloadStopwatch.ElapsedMilliseconds,
            });
            StatusMessage = "Settings reload failed. See logs for details.";
        }
    }

    private async Task SaveSecretAsync(SecretFieldViewModel? field)
    {
        if (field is null || string.IsNullOrWhiteSpace(field.Value))
            return;

        field.IsBusy = true;
        try
        {
            await _secretStore.SetAsync(field.Key, field.Value.Trim());
            field.Value = string.Empty;
            field.HasStoredValue = true;
            StatusMessage = $"Saved secret: {field.Label}";
        }
        catch (Exception ex)
        {
            Logger.LogError("Secrets", "Failed to save secret", ex, new Dictionary<string, object?>
            {
                ["secretKey"] = field.Key,
                ["label"] = field.Label,
            });
            StatusMessage = $"Failed to save secret: {field.Label}";
        }
        finally
        {
            field.IsBusy = false;
        }
    }

    private async Task ClearSecretAsync(SecretFieldViewModel? field)
    {
        if (field is null)
            return;

        field.IsBusy = true;
        try
        {
            await _secretStore.RemoveAsync(field.Key);
            field.Value = string.Empty;
            field.HasStoredValue = false;
            StatusMessage = $"Cleared secret: {field.Label}";
        }
        catch (Exception ex)
        {
            Logger.LogError("Secrets", "Failed to clear secret", ex, new Dictionary<string, object?>
            {
                ["secretKey"] = field.Key,
                ["label"] = field.Label,
            });
            StatusMessage = $"Failed to clear secret: {field.Label}";
        }
        finally
        {
            field.IsBusy = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _settings.SettingsChanged -= OnSettingsChanged;
        foreach (var field in Fields)
            field.PropertyChanged -= OnFieldPropertyChanged;
    }
}
