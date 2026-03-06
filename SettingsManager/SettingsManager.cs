using System.Text.Json;

namespace SettingsManager
{
    public class SettingsManager<T> : ISettingsManager<T> where T : class
    {
        public T Settings
        {
            get => field ?? new AppSettings("first_install", "first_install", "first_install", "first_install") as T;
            set;
        }

        private readonly string _settingsFilePath;

        public event Action<string, string>? SettingSaved;

        private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase // Use camelCase for JSON property names
        };

        public SettingsManager(string settingsFilePath, AppSettings settings)
        {
            Settings = settings as T;
            _settingsFilePath = settingsFilePath;
        }

        public void Save()
        {
            string json = JsonSerializer.Serialize(Settings, _jsonSerializerOptions);
            File.WriteAllText(_settingsFilePath, json);
        }

        public string Get(string settingName)
        {
            var property = typeof(T).GetProperty(settingName);
            if (property != null && property.PropertyType == typeof(string))
            {
                return (string?)property.GetValue(Settings) ?? string.Empty;
            }
            return string.Empty;
        }

        public void Set(string settingName, string settingValue)
        {
            var property = typeof(T).GetProperty(settingName);
            if (property != null && property.PropertyType == typeof(string))
            {
                property.SetValue(Settings, settingValue);
                OnSettingSaved(settingName, settingValue);
                Save();
            }
            else
            {
                throw new ArgumentException($"Setting with name '{settingName}' does not exist or is not of type string.");
            }
        }

        /*private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    Settings = JsonSerializer.Deserialize<T>(json, _jsonSerializerOptions) ?? throw new JsonException("Failed to deserialize settings.");
                }
                else
                {
                    Save(); // Create default settings file if it doesn't exist
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading settings from file '{_settingsFilePath}': {ex.Message}", ex);
            }
        }*/

        protected virtual void OnSettingSaved(string settingName, string settingValue)
        {
            SettingSaved?.Invoke(settingName, settingValue);
        }
    }
}
