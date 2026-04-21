using System.Text;

#if ANDROID
using Android.App;
using Android.OS;
using Android.Provider;
#endif

namespace ChartHub.Services;

public sealed class DeviceDisplayNameProvider : IDeviceDisplayNameProvider
{
    private const string UnknownDeviceName = "unknown-device";

    public string GetDisplayName()
    {
#if ANDROID
        string? userAssignedName = TryGetAndroidUserAssignedDeviceName();
        if (!string.IsNullOrWhiteSpace(userAssignedName))
        {
            return NormalizeDeviceName(userAssignedName, UnknownDeviceName);
        }

        return NormalizeDeviceName(GetAndroidManufacturerModel(), UnknownDeviceName);
#else
        return NormalizeDeviceName(Environment.MachineName, UnknownDeviceName);
#endif
    }

#if ANDROID
    private static string? TryGetAndroidUserAssignedDeviceName()
    {
        if (Application.Context?.ContentResolver is not { } resolver)
        {
            return null;
        }

        string? fromGlobal = Settings.Global.GetString(resolver, "device_name");
        if (!string.IsNullOrWhiteSpace(fromGlobal))
        {
            return fromGlobal;
        }

        string? fromSecureBluetoothName = Settings.Secure.GetString(resolver, "bluetooth_name");
        if (!string.IsNullOrWhiteSpace(fromSecureBluetoothName))
        {
            return fromSecureBluetoothName;
        }

        return null;
    }

    private static string GetAndroidManufacturerModel()
    {
        string manufacturer = Build.Manufacturer?.Trim() ?? string.Empty;
        string model = Build.Model?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(manufacturer) && string.IsNullOrWhiteSpace(model))
        {
            return UnknownDeviceName;
        }

        if (string.IsNullOrWhiteSpace(manufacturer))
        {
            return model;
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return manufacturer;
        }

        if (model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase))
        {
            return model;
        }

        return $"{manufacturer} {model}";
    }
#endif

    private static string NormalizeDeviceName(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        StringBuilder sb = new(raw.Length);
        bool previousWasWhitespace = false;

        foreach (char ch in raw.Trim())
        {
            if (char.IsControl(ch))
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasWhitespace)
                {
                    sb.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            sb.Append(ch);
            previousWasWhitespace = false;
        }

        string normalized = sb.ToString().Trim();
        if (normalized.Length == 0)
        {
            return fallback;
        }

        const int maxLength = 64;
        return normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }
}