using System.Globalization;
using System.Resources;

namespace ChartHub.Localization;

public static class UiLocalization
{
    private const string DefaultCulture = "en-US";
    private static readonly ResourceManager ResourceManager = new("ChartHub.Localization.UiStrings", typeof(UiLocalization).Assembly);

    public static string Get(string key)
    {
        string? value = ResourceManager.GetString(key, CultureInfo.CurrentUICulture);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    public static void ConfigureCulture(string? cultureName)
    {
        string normalizedCulture = string.IsNullOrWhiteSpace(cultureName)
            ? DefaultCulture
            : cultureName.Trim();

        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(normalizedCulture);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.GetCultureInfo(DefaultCulture);
        }

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
