using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

using Avalonia.Threading;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Metadata;
using ChartHub.Configuration.Models;
using ChartHub.Configuration.Secrets;
using ChartHub.Configuration.Stores;
using ChartHub.Localization;
using ChartHub.Services;
using ChartHub.Strings;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

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
    public bool IsDeveloperOnly { get; init; }

    public object SectionRef { get; init; } = default!;
    public PropertyInfo Property { get; init; } = default!;

    public bool IsTextEditor => EditorKind == SettingEditorKind.Text;
    public bool IsToggleEditor => EditorKind == SettingEditorKind.Toggle;
    public bool IsNumberEditor => EditorKind == SettingEditorKind.Number;
    public bool IsDropdownEditor => EditorKind == SettingEditorKind.Dropdown;
    public bool IsDirectoryPicker => EditorKind == SettingEditorKind.DirectoryPicker;
    public bool IsFilePicker => EditorKind == SettingEditorKind.FilePicker;
    public bool IsPathPicker => IsDirectoryPicker || IsFilePicker;
    public string BrowseButtonText => IsDirectoryPicker
        ? UiLocalization.Get("Settings.BrowseFolder")
        : UiLocalization.Get("Settings.BrowseFile");
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();

    public string StringValue
    {
        get => _stringValue;
        set
        {
            if (_stringValue == value)
            {
                return;
            }

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
            {
                return;
            }

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
            {
                return;
            }

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
            {
                return;
            }

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
            {
                return;
            }

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
            {
                return;
            }

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
            {
                return;
            }

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
            {
                return;
            }

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
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public bool HasDraftValue => !string.IsNullOrWhiteSpace(Value);

    public string StorageStatus => HasStoredValue
        ? UiLocalization.Get("Settings.StorageStored")
        : UiLocalization.Get("Settings.StorageNotSet");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{

    private readonly ISettingsOrchestrator _settings;
    public SettingsPageStrings PageStrings { get; } = new();
    private readonly ISecretStore _secretStore;
    private readonly AppGlobalSettings _globalSettings;
    private readonly IAuthSessionService _authSessionService;
    private readonly IChartHubServerApiClient _serverApiClient;
    private readonly Action<Action> _postToUi;
    private readonly bool _isAndroidPlatform;
    private readonly SemaphoreSlim _fieldUpdateLock = new(1, 1);

    private string _statusMessage = string.Empty;
    private bool _hasPendingRestartSettings;
    private bool _showDeveloperSettings;
    private bool _isTestingServerConnection;
    private bool _isAuthenticating;
    private bool _isResettingAuth;

    public ObservableCollection<SettingsFieldViewModel> Fields { get; } = [];
    public ObservableCollection<SecretFieldViewModel> Secrets { get; } = [];

    public IAsyncRelayCommand TestServerConnectionCommand { get; }
    public IAsyncRelayCommand SignInCommand { get; }
    public IAsyncRelayCommand ResetAuthSessionCommand { get; }
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
            {
                return;
            }

            _showDeveloperSettings = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSecretsPanelVisible));
            OnPropertyChanged(nameof(IsDeveloperDiagnosticsVisible));
            RebuildFieldsFrom(_settings.Current);
        }
    }

    public bool IsSecretsPanelVisible => IsDeveloperBuild && ShowDeveloperSettings;

    public bool IsDeveloperDiagnosticsVisible => IsDeveloperBuild && ShowDeveloperSettings;

    public string ServerApiBaseUrl
    {
        get => _globalSettings.ServerApiBaseUrl;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(_globalSettings.ServerApiBaseUrl, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _globalSettings.ServerApiBaseUrl = normalized;
            StatusMessage = UiLocalization.Get("Settings.ServerBaseUrlSaved");
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasServerApiBaseUrl));
        }
    }

    public bool HasServerApiBaseUrl => !string.IsNullOrWhiteSpace(ServerApiBaseUrl);

    public string ServerApiAuthToken => _globalSettings.ServerApiAuthToken;

    public bool HasServerApiAuthToken => !string.IsNullOrWhiteSpace(ServerApiAuthToken);

    public bool HasNoServerApiAuthToken => !HasServerApiAuthToken;

    public bool IsTestingServerConnection
    {
        get => _isTestingServerConnection;
        private set
        {
            if (_isTestingServerConnection == value)
            {
                return;
            }

            _isTestingServerConnection = value;
            OnPropertyChanged();
            TestServerConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsAuthenticating
    {
        get => _isAuthenticating;
        private set
        {
            if (_isAuthenticating == value)
            {
                return;
            }

            _isAuthenticating = value;
            OnPropertyChanged();
            SignInCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsResettingAuth
    {
        get => _isResettingAuth;
        private set
        {
            if (_isResettingAuth == value)
            {
                return;
            }

            _isResettingAuth = value;
            OnPropertyChanged();
            ResetAuthSessionCommand.NotifyCanExecuteChanged();
        }
    }

    public string AuthStatusText => _authSessionService.CurrentState switch
    {
        AuthSessionState.Authenticated when !string.IsNullOrWhiteSpace(_authSessionService.SignedInEmail)
            => UiLocalization.Format("Settings.AuthSignedInAs", _authSessionService.SignedInEmail),
        AuthSessionState.Authenticated => UiLocalization.Get("Settings.AuthSignedIn"),
        AuthSessionState.Authenticating => UiLocalization.Get("Settings.Working"),
        AuthSessionState.Expired => UiLocalization.Get("Settings.AuthExpired"),
        AuthSessionState.Unknown => UiLocalization.Get("Settings.AuthChecking"),
        _ => UiLocalization.Get("Settings.AuthSignedOut"),
    };

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage == value)
            {
                return;
            }

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
            {
                return;
            }

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

    public SettingsViewModel(
        ISettingsOrchestrator settings,
        ISecretStore secretStore,
        AppGlobalSettings globalSettings,
        IAuthSessionService authSessionService,
        IChartHubServerApiClient serverApiClient)
        : this(settings, secretStore, globalSettings, authSessionService, serverApiClient, action => Dispatcher.UIThread.Post(action), null)
    {
    }

    internal SettingsViewModel(
        ISettingsOrchestrator settings,
        ISecretStore secretStore,
        AppGlobalSettings globalSettings,
        IAuthSessionService authSessionService,
        IChartHubServerApiClient serverApiClient,
        Action<Action> postToUi,
        bool? isAndroidPlatform)
    {
        _settings = settings;
        _secretStore = secretStore;
        _globalSettings = globalSettings;
        _authSessionService = authSessionService;
        _serverApiClient = serverApiClient;
        _postToUi = postToUi;
        _isAndroidPlatform = isAndroidPlatform ?? OperatingSystem.IsAndroid();
        _showDeveloperSettings = false;

        TestServerConnectionCommand = new AsyncRelayCommand(TestServerConnectionAsync, CanTestServerConnection);
        SignInCommand = new AsyncRelayCommand(SignInAsync, CanSignIn);
        ResetAuthSessionCommand = new AsyncRelayCommand(ResetAuthSessionAsync, CanResetAuthSession);
        SaveSecretCommand = new AsyncRelayCommand<SecretFieldViewModel?>(SaveSecretAsync);
        ClearSecretCommand = new AsyncRelayCommand<SecretFieldViewModel?>(ClearSecretAsync);
        SecretStorageBackend = ResolveSecretStorageBackend(secretStore);

        RebuildFieldsFrom(_settings.Current);
        if (IsDeveloperBuild)
        {
            BuildSecrets();
        }

        _settings.SettingsChanged += OnSettingsChanged;
        _globalSettings.PropertyChanged += OnGlobalSettingsPropertyChanged;
        _authSessionService.PropertyChanged += OnAuthSessionPropertyChanged;
        _authSessionService.SessionStateChanged += OnAuthSessionStateChanged;
        if (IsDeveloperBuild)
        {
            _ = RefreshSecretStateAsync();
        }
    }

    private static string ResolveSecretStorageBackend(ISecretStore secretStore)
    {
        if (secretStore is DesktopSecretStore desktopSecretStore)
        {
            return $"Desktop ({desktopSecretStore.BackendName})";
        }

        if (OperatingSystem.IsAndroid())
        {
            return "Android (Keystore)";
        }

        return secretStore.GetType().Name;
    }

    private void OnSettingsChanged(AppConfigRoot config)
    {
        _postToUi(() =>
        {
            RebuildFieldsFrom(config);
            RaiseServerStateChanged();
        });
    }

    private void OnGlobalSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppGlobalSettings.ServerApiBaseUrl)
            or nameof(AppGlobalSettings.ServerApiAuthToken)
            or nameof(AppGlobalSettings.DeviceDisplayNameOverride))
        {
            _postToUi(RaiseServerStateChanged);
        }
    }

    private void OnAuthSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IAuthSessionService.CurrentState)
            or nameof(IAuthSessionService.SignedInEmail)
            or nameof(IAuthSessionService.CurrentAccessToken))
        {
            _postToUi(RaiseServerStateChanged);
        }
    }

    private void OnAuthSessionStateChanged(object? sender, EventArgs e)
    {
        _postToUi(RaiseServerStateChanged);
    }

    private void RebuildFieldsFrom(AppConfigRoot config)
    {
        foreach (SettingsFieldViewModel field in Fields)
        {
            field.PropertyChanged -= OnFieldPropertyChanged;
        }

        var fieldBuffer = new List<SettingsFieldViewModel>();
        AddSectionFields(fieldBuffer, config.Runtime, "Runtime");
        AddSectionFields(fieldBuffer, config.GoogleAuth, "GoogleAuth");

        fieldBuffer = fieldBuffer
            .Where(f => ShowDeveloperSettings || !f.IsDeveloperOnly)
            .OrderBy(GetGroupOrder)
            .ThenBy(f => f.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string previousGroup = string.Empty;
        foreach (SettingsFieldViewModel field in fieldBuffer)
        {
            field.IsGroupHeaderVisible = !string.Equals(previousGroup, field.Group, StringComparison.OrdinalIgnoreCase);
            previousGroup = field.Group;
        }

        Fields.Clear();
        foreach (SettingsFieldViewModel field in fieldBuffer)
        {
            field.PropertyChanged += OnFieldPropertyChanged;
            Fields.Add(field);
        }

        HasPendingRestartSettings = Fields.Any(f => !f.IsHotReloadable);
        RefreshSaveState();
    }

    private void OnFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SettingsFieldViewModel field)
        {
            return;
        }

        if (e.PropertyName is not nameof(SettingsFieldViewModel.StringValue)
            and not nameof(SettingsFieldViewModel.BoolValue)
            and not nameof(SettingsFieldViewModel.NumberValue)
            and not nameof(SettingsFieldViewModel.SelectedOption))
        {
            return;
        }

        RevalidateFieldLive(field);
        RefreshSaveState();

        if (field.HasError || !FieldHasPendingChange(field))
        {
            return;
        }

        _ = PersistFieldChangeAsync(field);
    }

    private static void RevalidateFieldLive(SettingsFieldViewModel field)
    {
        if (!field.IsPathPicker)
        {
            if (field.HasError)
            {
                field.ErrorMessage = string.Empty;
            }

            return;
        }

        field.ErrorMessage = ValidateDraftField(field) ?? string.Empty;
    }

    private void BuildSecrets()
    {
        Secrets.Clear();
        Secrets.Add(new SecretFieldViewModel
        {
            Label = UiLocalization.Get("Settings.Secret.GoogleDesktopClientSecretLabel"),
            Key = SecretKeys.GoogleDesktopClientSecret,
            Description = UiLocalization.Get("Settings.Secret.GoogleDesktopClientSecretDescription"),
        });
    }

    private async Task RefreshSecretStateAsync()
    {
        foreach (SecretFieldViewModel secret in Secrets)
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

    private void AddSectionFields(List<SettingsFieldViewModel> destination, object section, string sectionName)
    {
        IEnumerable<PropertyInfo> properties = section.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.CanWrite);

        foreach (PropertyInfo? property in properties)
        {
            if (property.GetCustomAttribute<SettingHiddenAttribute>() is not null)
            {
                continue;
            }

            if (!IsVisibleOnCurrentPlatform(property))
            {
                continue;
            }

            string display = property.GetCustomAttribute<SettingDisplayAttribute>()?.Label ?? property.Name;
            string group = property.GetCustomAttribute<SettingGroupAttribute>()?.Name ?? "General";
            string description = property.GetCustomAttribute<SettingDescriptionAttribute>()?.Text ?? string.Empty;
            SettingEditorKind editorKind = ResolveEditorKind(property);
            bool isHotReloadable = property.GetCustomAttribute<SettingHotReloadableAttribute>()?.IsHotReloadable ?? false;
            bool requiresRestart = property.GetCustomAttribute<SettingRequiresRestartAttribute>() is not null;
            bool isDeveloperOnly = property.GetCustomAttribute<SettingDeveloperOnlyAttribute>() is not null;
            IReadOnlyList<string> options = ResolveOptions(property, editorKind);

            var field = new SettingsFieldViewModel
            {
                Group = group,
                Label = display,
                Description = description,
                Key = $"{sectionName}.{property.Name}",
                EditorKind = editorKind,
                IsHotReloadable = isHotReloadable,
                RequiresRestart = requiresRestart,
                IsDeveloperOnly = isDeveloperOnly,
                SectionRef = section,
                Property = property,
                Options = options,
            };

            object? value = property.GetValue(section);
            if (property.PropertyType == typeof(bool))
            {
                field.BoolValue = value is bool b && b;
            }
            else if (field.EditorKind == SettingEditorKind.Number)
            {
                field.NumberValue = ToDouble(value);
            }
            else if (field.EditorKind == SettingEditorKind.Dropdown)
            {
                string currentOption = value?.ToString() ?? string.Empty;
                field.SelectedOption = string.IsNullOrWhiteSpace(currentOption)
                    ? field.Options.FirstOrDefault() ?? string.Empty
                    : currentOption;
            }
            else
            {
                field.StringValue = value?.ToString() ?? string.Empty;
            }

            destination.Add(field);
        }
    }

    private bool IsVisibleOnCurrentPlatform(PropertyInfo property)
    {
        SettingPlatformsAttribute? platformAttribute = property.GetCustomAttribute<SettingPlatformsAttribute>();
        if (platformAttribute is null || platformAttribute.Targets is SettingPlatformTargets.Shared)
        {
            return true;
        }

        return _isAndroidPlatform
            ? platformAttribute.Targets.HasFlag(SettingPlatformTargets.Android)
            : platformAttribute.Targets.HasFlag(SettingPlatformTargets.Desktop);
    }

    private static SettingEditorKind ResolveEditorKind(PropertyInfo property)
    {
        SettingEditorKind? explicitEditor = property.GetCustomAttribute<SettingEditorAttribute>()?.EditorKind;
        if (explicitEditor.HasValue)
        {
            return explicitEditor.Value;
        }

        if (property.PropertyType.IsEnum)
        {
            return SettingEditorKind.Dropdown;
        }

        if (IsNumericType(property.PropertyType))
        {
            return SettingEditorKind.Number;
        }

        return property.PropertyType == typeof(bool)
            ? SettingEditorKind.Toggle
            : SettingEditorKind.Text;
    }

    private static IReadOnlyList<string> ResolveOptions(PropertyInfo property, SettingEditorKind editorKind)
    {
        if (editorKind == SettingEditorKind.Dropdown && property.PropertyType.IsEnum)
        {
            return Enum.GetNames(property.PropertyType);
        }

        return Array.Empty<string>();
    }

    private static bool IsNumericType(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
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
        {
            return 0;
        }

        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private async Task PersistFieldChangeAsync(SettingsFieldViewModel field)
    {
        string? validationError = ValidateDraftField(field);
        field.ErrorMessage = validationError ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            StatusMessage = UiLocalization.Get("Settings.ValidationFailed");
            RefreshSaveState();
            return;
        }

        await _fieldUpdateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!FieldHasPendingChange(field))
            {
                return;
            }

            ConfigValidationResult result = await _settings.UpdateAsync(config => ApplyField(config, field)).ConfigureAwait(false);
            if (!result.IsValid)
            {
                ConfigValidationFailure? failure = result.Failures.FirstOrDefault(f => string.Equals(f.Key, field.Key, StringComparison.Ordinal));
                field.ErrorMessage = failure?.Message ?? UiLocalization.Get("Settings.ValidationFailed");
                StatusMessage = UiLocalization.Get("Settings.ValidationFailed");
                RefreshSaveState();
                return;
            }

            if (!field.IsHotReloadable)
            {
                await _settings.ReloadAsync().ConfigureAwait(false);
                StatusMessage = UiLocalization.Format("Settings.FieldSavedRestartRequired", field.Label);
            }
            else
            {
                StatusMessage = UiLocalization.Format("Settings.FieldSaved", field.Label);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Config", "Immediate field save failed unexpectedly", ex, new Dictionary<string, object?>
            {
                ["fieldKey"] = field.Key,
                ["label"] = field.Label,
            });
            StatusMessage = UiLocalization.Format("Settings.FieldSaveFailed", field.Label);
        }
        finally
        {
            RefreshSaveState();
            _fieldUpdateLock.Release();
        }
    }

    private void RefreshSaveState()
    {
        HasPendingRestartSettings = false;
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(ValidationIssueSummaries));
    }

    private static bool FieldHasPendingChange(SettingsFieldViewModel field)
    {
        object? currentValue = field.Property.GetValue(field.SectionRef);

        if (field.Property.PropertyType == typeof(bool))
        {
            return (currentValue is bool currentBool ? currentBool : false) != field.BoolValue;
        }

        if (field.EditorKind == SettingEditorKind.Number)
        {
            double draft = field.NumberValue;
            double current = ToDouble(currentValue);
            return Math.Abs(draft - current) > double.Epsilon;
        }

        if (field.EditorKind == SettingEditorKind.Dropdown)
        {
            string draftOption = string.IsNullOrWhiteSpace(field.SelectedOption)
                ? field.Options.FirstOrDefault() ?? string.Empty
                : field.SelectedOption;
            string currentOption = currentValue?.ToString() ?? string.Empty;

            if (field.Property.PropertyType == typeof(string)
                && string.IsNullOrWhiteSpace(currentOption)
                && field.Options.Count > 0
                && string.Equals(draftOption, field.Options[0], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !string.Equals(draftOption, currentOption, StringComparison.OrdinalIgnoreCase);
        }

        string draftText = field.StringValue ?? string.Empty;
        string currentText = currentValue?.ToString() ?? string.Empty;
        return !string.Equals(draftText, currentText, StringComparison.Ordinal);
    }

    private static string? ValidateDraftField(SettingsFieldViewModel field)
    {
        if (!field.IsPathPicker)
        {
            return null;
        }

        string value = field.StringValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return UiLocalization.Get("Settings.PathRequired");
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
        {
            return UiLocalization.Get("Settings.PathMustBeLocal");
        }

        string path = value;
        if (field.IsDirectoryPicker)
        {
            return ValidateDirectoryPath(path);
        }

        if (field.IsFilePicker)
        {
            return ValidateFilePath(path);
        }

        return null;
    }


    private static string? ValidateDirectoryPath(string path)
    {
        if (Directory.Exists(path))
        {
            return null;
        }

        if (File.Exists(path))
        {
            return UiLocalization.Get("Settings.DirectoryExpected");
        }

        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
        {
            return null;
        }

        return UiLocalization.Get("Settings.DirectoryParentMissing");
    }

    private static string? ValidateFilePath(string path)
    {
        if (File.Exists(path))
        {
            return null;
        }

        if (Directory.Exists(path))
        {
            return UiLocalization.Get("Settings.FileExpected");
        }

        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
        {
            return null;
        }

        return UiLocalization.Get("Settings.FileParentMissing");
    }

    private static void ApplyField(AppConfigRoot config, SettingsFieldViewModel field)
    {
        object section = field.Key.StartsWith("Runtime.", StringComparison.Ordinal)
            ? (object)config.Runtime
            : config.GoogleAuth;

        if (field.Property.PropertyType == typeof(bool))
        {
            field.Property.SetValue(section, field.BoolValue);
            return;
        }

        if (field.EditorKind == SettingEditorKind.Number)
        {
            Type targetType = Nullable.GetUnderlyingType(field.Property.PropertyType) ?? field.Property.PropertyType;
            object converted = Convert.ChangeType(field.NumberValue, targetType, CultureInfo.InvariantCulture);
            field.Property.SetValue(section, converted);
            return;
        }

        if (field.EditorKind == SettingEditorKind.Dropdown && field.Property.PropertyType.IsEnum)
        {
            string option = string.IsNullOrWhiteSpace(field.SelectedOption)
                ? field.Options.FirstOrDefault() ?? string.Empty
                : field.SelectedOption;
            object parsed = Enum.Parse(field.Property.PropertyType, option, ignoreCase: true);
            field.Property.SetValue(section, parsed);
            return;
        }

        if (field.EditorKind == SettingEditorKind.Dropdown && field.Property.PropertyType == typeof(string))
        {
            string option = string.IsNullOrWhiteSpace(field.SelectedOption)
                ? field.Options.FirstOrDefault() ?? string.Empty
                : field.SelectedOption;
            field.Property.SetValue(section, option);
            return;
        }

        if (field.Property.PropertyType == typeof(string))
        {
            field.Property.SetValue(section, field.StringValue?.Trim() ?? string.Empty);
        }
    }

    private static int GetGroupOrder(SettingsFieldViewModel field)
    {
        return field.Group switch
        {
            "General" => 0,
            "Input & Remote" => 1,
            "Developer (Google OAuth)" => 2,
            _ => 100,
        };
    }

    private bool CanTestServerConnection()
    {
        return !IsTestingServerConnection && !string.IsNullOrWhiteSpace(ServerApiBaseUrl);
    }

    private async Task TestServerConnectionAsync()
    {
        if (!CanTestServerConnection())
        {
            StatusMessage = UiLocalization.Get("Settings.ServerConnectionMissingUrl");
            return;
        }

        IsTestingServerConnection = true;
        try
        {
            await _serverApiClient.GetHealthAsync(ServerApiBaseUrl).ConfigureAwait(false);
            StatusMessage = UiLocalization.Get("Settings.ServerConnectionSucceeded");
        }
        catch (Exception ex)
        {
            Logger.LogError("Config", "Server connection test failed", ex, new Dictionary<string, object?>
            {
                ["baseUrl"] = ServerApiBaseUrl,
            });
            StatusMessage = UiLocalization.Format("Settings.ServerConnectionFailed", ex.Message);
        }
        finally
        {
            IsTestingServerConnection = false;
        }
    }

    private bool CanSignIn()
    {
        return !IsAuthenticating && !string.IsNullOrWhiteSpace(ServerApiBaseUrl);
    }

    private async Task SignInAsync()
    {
        if (!CanSignIn())
        {
            StatusMessage = UiLocalization.Get("Settings.ServerConnectionMissingUrl");
            return;
        }

        IsAuthenticating = true;
        try
        {
            await _authSessionService.SignInAsync().ConfigureAwait(false);
            RaiseServerStateChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError("Auth", "Interactive sign-in failed from settings", ex);
            StatusMessage = UiLocalization.Format("Settings.SignInFailed", ex.Message);
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    private bool CanResetAuthSession()
    {
        return !IsResettingAuth && (_authSessionService.CurrentState == AuthSessionState.Authenticated || HasServerApiAuthToken);
    }

    private async Task ResetAuthSessionAsync()
    {
        if (!CanResetAuthSession())
        {
            return;
        }

        IsResettingAuth = true;
        try
        {
            await _authSessionService.SignOutAsync().ConfigureAwait(false);
            StatusMessage = UiLocalization.Get("Settings.AuthResetSucceeded");
            RaiseServerStateChanged();
        }
        catch (Exception ex)
        {
            Logger.LogError("Auth", "Failed to clear saved auth session", ex);
            StatusMessage = UiLocalization.Format("Settings.AuthResetFailed", ex.Message);
        }
        finally
        {
            IsResettingAuth = false;
        }
    }

    private void RaiseServerStateChanged()
    {
        OnPropertyChanged(nameof(ServerApiBaseUrl));
        OnPropertyChanged(nameof(HasServerApiBaseUrl));
        OnPropertyChanged(nameof(ServerApiAuthToken));
        OnPropertyChanged(nameof(HasServerApiAuthToken));
        OnPropertyChanged(nameof(HasNoServerApiAuthToken));
        OnPropertyChanged(nameof(AuthStatusText));
        TestServerConnectionCommand.NotifyCanExecuteChanged();
        SignInCommand.NotifyCanExecuteChanged();
        ResetAuthSessionCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveSecretAsync(SecretFieldViewModel? field)
    {
        if (field is null || string.IsNullOrWhiteSpace(field.Value))
        {
            return;
        }

        field.IsBusy = true;
        try
        {
            await _secretStore.SetAsync(field.Key, field.Value.Trim());
            field.Value = string.Empty;
            field.HasStoredValue = true;
            StatusMessage = UiLocalization.Format("Settings.SecretSaved", field.Label);
        }
        catch (Exception ex)
        {
            Logger.LogError("Secrets", "Failed to save secret", ex, new Dictionary<string, object?>
            {
                ["secretKey"] = field.Key,
                ["label"] = field.Label,
            });
            StatusMessage = UiLocalization.Format("Settings.SecretSaveFailed", field.Label);
        }
        finally
        {
            field.IsBusy = false;
        }
    }

    private async Task ClearSecretAsync(SecretFieldViewModel? field)
    {
        if (field is null)
        {
            return;
        }

        field.IsBusy = true;
        try
        {
            await _secretStore.RemoveAsync(field.Key);
            field.Value = string.Empty;
            field.HasStoredValue = false;
            StatusMessage = UiLocalization.Format("Settings.SecretCleared", field.Label);
        }
        catch (Exception ex)
        {
            Logger.LogError("Secrets", "Failed to clear secret", ex, new Dictionary<string, object?>
            {
                ["secretKey"] = field.Key,
                ["label"] = field.Label,
            });
            StatusMessage = UiLocalization.Format("Settings.SecretClearFailed", field.Label);
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
        _globalSettings.PropertyChanged -= OnGlobalSettingsPropertyChanged;
        _authSessionService.PropertyChanged -= OnAuthSessionPropertyChanged;
        _authSessionService.SessionStateChanged -= OnAuthSessionStateChanged;
        foreach (SettingsFieldViewModel field in Fields)
        {
            field.PropertyChanged -= OnFieldPropertyChanged;
        }

        _fieldUpdateLock.Dispose();
    }
}
