using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
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

using Google.Apis.Auth.OAuth2;

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
    private readonly IGoogleAuthProvider _googleAuthProvider;
    private readonly IChartHubServerApiClient _serverApiClient;
    private readonly Action<Action> _postToUi;
    private readonly bool _isAndroidPlatform;
    private static readonly string[] ServerAuthenticationScopes = ["openid", "email", "profile"];

    private string _statusMessage = "";
    private bool _hasPendingRestartSettings;
    private bool _isSaving;
    private bool _showDeveloperSettings;
    private bool _isServerAuthenticationBusy;
    private string _serverAuthenticationStatusMessage = UiLocalization.Get("Settings.NotAuthenticated");
    private string? _serverAuthenticationErrorMessage;

    public ObservableCollection<SettingsFieldViewModel> Fields { get; } = [];
    public ObservableCollection<SecretFieldViewModel> Secrets { get; } = [];

    public AsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand<SecretFieldViewModel?> SaveSecretCommand { get; }
    public IAsyncRelayCommand<SecretFieldViewModel?> ClearSecretCommand { get; }
    public IAsyncRelayCommand AuthenticateServerCommand { get; }
    public string SecretStorageBackend { get; }

    public bool IsServerAuthenticationBusy
    {
        get => _isServerAuthenticationBusy;
        private set
        {
            if (_isServerAuthenticationBusy == value)
            {
                return;
            }

            _isServerAuthenticationBusy = value;
            OnPropertyChanged();
            AuthenticateServerCommand.NotifyCanExecuteChanged();
        }
    }

    public string ServerAuthenticationStatusMessage
    {
        get => _serverAuthenticationStatusMessage;
        private set
        {
            if (_serverAuthenticationStatusMessage == value)
            {
                return;
            }

            _serverAuthenticationStatusMessage = value;
            OnPropertyChanged();
        }
    }

    public string? ServerAuthenticationErrorMessage
    {
        get => _serverAuthenticationErrorMessage;
        private set
        {
            if (_serverAuthenticationErrorMessage == value)
            {
                return;
            }

            _serverAuthenticationErrorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasServerAuthenticationError));
        }
    }

    public bool HasServerAuthenticationError => !string.IsNullOrWhiteSpace(ServerAuthenticationErrorMessage);

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
        }
    }

    public bool IsSecretsPanelVisible => IsDeveloperBuild && ShowDeveloperSettings;

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
        IGoogleAuthProvider googleAuthProvider,
        IChartHubServerApiClient serverApiClient)
        : this(settings, secretStore, googleAuthProvider, serverApiClient, action => Dispatcher.UIThread.Post(action), null)
    {
    }

    internal SettingsViewModel(
        ISettingsOrchestrator settings,
        ISecretStore secretStore,
        IGoogleAuthProvider googleAuthProvider,
        IChartHubServerApiClient serverApiClient,
        Action<Action> postToUi,
        bool? isAndroidPlatform)
    {
        _settings = settings;
        _secretStore = secretStore;
        _googleAuthProvider = googleAuthProvider;
        _serverApiClient = serverApiClient;
        _postToUi = postToUi;
        _isAndroidPlatform = isAndroidPlatform ?? OperatingSystem.IsAndroid();
        _showDeveloperSettings = false;

        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        SaveSecretCommand = new AsyncRelayCommand<SecretFieldViewModel?>(SaveSecretAsync);
        ClearSecretCommand = new AsyncRelayCommand<SecretFieldViewModel?>(ClearSecretAsync);
        AuthenticateServerCommand = new AsyncRelayCommand(AuthenticateServerAsync, CanAuthenticateServer);
        SecretStorageBackend = ResolveSecretStorageBackend(secretStore);
        UpdateServerAuthenticationStatusFromCurrentSettings();

        RebuildFieldsFrom(_settings.Current);
        if (IsDeveloperBuild)
        {
            BuildSecrets();
        }

        _settings.SettingsChanged += OnSettingsChanged;
        if (IsDeveloperBuild)
        {
            _ = RefreshSecretStateAsync();
        }
    }

    private bool CanAuthenticateServer() => !IsServerAuthenticationBusy;

    private async Task AuthenticateServerAsync()
    {
        if (!CanAuthenticateServer())
        {
            return;
        }

        string baseUrl = ResolveServerApiBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            await RunOnUiAsync(() =>
            {
                ServerAuthenticationErrorMessage = UiLocalization.Get("Settings.AuthBaseUrlRequired");
                ServerAuthenticationStatusMessage = UiLocalization.Get("Settings.AuthNotConfigured");
            }).ConfigureAwait(false);
            return;
        }

        await RunOnUiAsync(() =>
        {
            IsServerAuthenticationBusy = true;
            ServerAuthenticationErrorMessage = null;
            ServerAuthenticationStatusMessage = UiLocalization.Get("Settings.AuthOpeningGoogleSignIn");
        }).ConfigureAwait(false);

        try
        {
            UserCredential credential = await _googleAuthProvider.AuthorizeInteractiveAsync(ServerAuthenticationScopes).ConfigureAwait(false);
            ChartHubServerAuthExchangeResponse exchange = await ExchangeTokenWithRetryAsync(baseUrl, credential).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(exchange.AccessToken))
            {
                throw new InvalidOperationException("ChartHub Server returned an empty access token.");
            }

            ConfigValidationResult result = await _settings.UpdateAsync(config =>
            {
                config.Runtime.ServerApiAuthToken = exchange.AccessToken;
            }).ConfigureAwait(false);

            if (!result.IsValid)
            {
                throw new InvalidOperationException("Failed to store ChartHub Server access token in settings.");
            }

            await RunOnUiAsync(() =>
            {
                ServerAuthenticationStatusMessage = UiLocalization.Get("Settings.AuthSucceeded");
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            string errorMessage = BuildServerAuthenticationErrorMessage(ex);
            await RunOnUiAsync(() =>
            {
                ServerAuthenticationErrorMessage = errorMessage;
                ServerAuthenticationStatusMessage = UiLocalization.Get("Settings.AuthFailed");
            }).ConfigureAwait(false);
            Logger.LogError("Auth", "ChartHub Server authentication failed", ex);
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsServerAuthenticationBusy = false;
            }).ConfigureAwait(false);
        }
    }

    private async Task<ChartHubServerAuthExchangeResponse> ExchangeTokenWithRetryAsync(string baseUrl, UserCredential credential)
    {
        string googleIdToken = GetGoogleIdTokenOrThrow(credential);
        await RunOnUiAsync(() =>
        {
            ServerAuthenticationStatusMessage = UiLocalization.Get("Settings.AuthExchangingGoogleToken");
        }).ConfigureAwait(false);

        try
        {
            return await _serverApiClient.ExchangeGoogleTokenAsync(baseUrl, googleIdToken).ConfigureAwait(false);
        }
        catch (ChartHubServerApiException ex) when (IsInvalidGoogleIdTokenError(ex))
        {
            await RunOnUiAsync(() =>
            {
                ServerAuthenticationStatusMessage = UiLocalization.Get("Settings.AuthTokenExpired");
            }).ConfigureAwait(false);
            await _googleAuthProvider.SignOutAsync(credential).ConfigureAwait(false);

            UserCredential refreshedCredential = await _googleAuthProvider
                .AuthorizeInteractiveAsync(ServerAuthenticationScopes)
                .ConfigureAwait(false);
            string refreshedGoogleIdToken = GetGoogleIdTokenOrThrow(refreshedCredential);

            await RunOnUiAsync(() =>
            {
                ServerAuthenticationStatusMessage = UiLocalization.Get("Settings.AuthRetryExchange");
            }).ConfigureAwait(false);
            return await _serverApiClient.ExchangeGoogleTokenAsync(baseUrl, refreshedGoogleIdToken).ConfigureAwait(false);
        }
    }

    private Task RunOnUiAsync(Action action)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _postToUi(() =>
        {
            try
            {
                action();
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    private static string GetGoogleIdTokenOrThrow(UserCredential credential)
    {
        string? googleIdToken = credential.Token.IdToken;
        if (string.IsNullOrWhiteSpace(googleIdToken))
        {
            throw new InvalidOperationException("Google sign-in succeeded but did not return an ID token.");
        }

        return googleIdToken;
    }

    private static bool IsInvalidGoogleIdTokenError(ChartHubServerApiException exception)
    {
        return exception.StatusCode == HttpStatusCode.BadRequest
            && string.Equals(exception.ErrorCode, "invalid_google_id_token", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildServerAuthenticationErrorMessage(Exception exception)
    {
        if (exception is ChartHubServerApiException apiException)
        {
            if (IsInvalidGoogleIdTokenError(apiException))
            {
                return "Google sign-in token was rejected by ChartHub Server. Sign in again and verify server audience configuration.";
            }

            if (apiException.StatusCode == HttpStatusCode.Forbidden)
            {
                return "Your Google account is not allowlisted in ChartHub Server Auth:AllowedEmails.";
            }

            if (apiException.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                return "ChartHub Server could not reach Google token validation service. Try again shortly.";
            }
        }

        return exception.Message;
    }

    private string ResolveServerApiBaseUrl()
    {
        SettingsFieldViewModel? baseUrlField = Fields.FirstOrDefault(field =>
            string.Equals(field.Key, "Runtime.ServerApiBaseUrl", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(baseUrlField?.StringValue))
        {
            return baseUrlField.StringValue.Trim();
        }

        return _settings.Current.Runtime.ServerApiBaseUrl?.Trim() ?? string.Empty;
    }

    private void UpdateServerAuthenticationStatusFromCurrentSettings()
    {
        bool hasToken = !string.IsNullOrWhiteSpace(_settings.Current.Runtime.ServerApiAuthToken);
        ServerAuthenticationStatusMessage = hasToken
            ? UiLocalization.Get("Settings.AuthTokenConfigured")
            : UiLocalization.Get("Settings.NotAuthenticated");

        if (!hasToken)
        {
            ServerAuthenticationErrorMessage = null;
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
            UpdateServerAuthenticationStatusFromCurrentSettings();
        });
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
            .OrderBy(f => f.Group, StringComparer.OrdinalIgnoreCase)
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

        HasPendingRestartSettings = Fields.Any(FieldRequiresReloadAfterSave);
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
        Secrets.Add(new SecretFieldViewModel
        {
            Label = UiLocalization.Get("Settings.Secret.GoogleRefreshTokenLabel"),
            Key = SecretKeys.GoogleRefreshToken,
            Description = UiLocalization.Get("Settings.Secret.GoogleRefreshTokenDescription"),
        });
        Secrets.Add(new SecretFieldViewModel
        {
            Label = UiLocalization.Get("Settings.Secret.GoogleAccessTokenLabel"),
            Key = SecretKeys.GoogleAccessToken,
            Description = UiLocalization.Get("Settings.Secret.GoogleAccessTokenDescription"),
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

    private async Task SaveAsync()
    {
        if (!CanSave())
        {
            return;
        }

        var saveStopwatch = Stopwatch.StartNew();
        Logger.LogInfo("Config", "Settings save started", new Dictionary<string, object?>
        {
            ["fieldCount"] = Fields.Count,
        });

        _isSaving = true;
        RefreshSaveState();
        try
        {
            foreach (SettingsFieldViewModel field in Fields)
            {
                field.ErrorMessage = string.Empty;
            }

            bool hasLocalValidationFailures = false;
            foreach (SettingsFieldViewModel field in Fields)
            {
                string? validationError = ValidateDraftField(field);
                if (string.IsNullOrWhiteSpace(validationError))
                {
                    continue;
                }

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
                StatusMessage = UiLocalization.Get("Settings.ValidationFailed");
                return;
            }

            bool requiresReloadAfterSave = Fields.Any(FieldRequiresReloadAfterSave);

            ConfigValidationResult result = await _settings.UpdateAsync(config =>
            {
                foreach (SettingsFieldViewModel field in Fields)
                {
                    ApplyField(config, field);
                }
            });

            if (!result.IsValid)
            {
                foreach (ConfigValidationFailure failure in result.Failures)
                {
                    SettingsFieldViewModel? field = Fields.FirstOrDefault(f => string.Equals(f.Key, failure.Key, StringComparison.Ordinal));
                    if (field is not null)
                    {
                        field.ErrorMessage = failure.Message;
                    }
                }

                Logger.LogWarning("Config", "Settings validation failed during save", new Dictionary<string, object?>
                {
                    ["fieldKeys"] = string.Join(",", result.Failures.Select(f => f.Key)),
                    ["elapsedMs"] = saveStopwatch.ElapsedMilliseconds,
                });
                StatusMessage = UiLocalization.Get("Settings.ValidationFailed");
                return;
            }

            Logger.LogInfo("Config", "Settings save completed", new Dictionary<string, object?>
            {
                ["fieldCount"] = Fields.Count,
                ["elapsedMs"] = saveStopwatch.ElapsedMilliseconds,
            });

            if (requiresReloadAfterSave)
            {
                await _settings.ReloadAsync();
                StatusMessage = UiLocalization.Get("Settings.SavedReloaded");
            }
            else
            {
                StatusMessage = UiLocalization.Get("Settings.Saved");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Config", "Settings save failed unexpectedly", ex, new Dictionary<string, object?>
            {
                ["fieldCount"] = Fields.Count,
                ["elapsedMs"] = saveStopwatch.ElapsedMilliseconds,
            });
            StatusMessage = UiLocalization.Get("Settings.SaveFailed");
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
        HasPendingRestartSettings = Fields.Any(FieldRequiresReloadAfterSave);
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(ValidationIssueSummaries));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private static bool FieldRequiresReloadAfterSave(SettingsFieldViewModel field)
    {
        return !field.IsHotReloadable && FieldHasPendingChange(field);
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
            field.Property.SetValue(section, field.StringValue);
        }
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
        foreach (SettingsFieldViewModel field in Fields)
        {
            field.PropertyChanged -= OnFieldPropertyChanged;
        }
    }
}
