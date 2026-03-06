namespace SettingsManager
{
    public interface ISettingsManager<T> where T : class
    {
        event Action<string, string>? SettingSaved;
        T Settings { get; set; }
        void Save();
        string Get(string settingName);
        void Set(string settingName, string settingValue);
    }
}
