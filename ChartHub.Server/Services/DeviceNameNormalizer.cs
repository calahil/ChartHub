using System.Text;

namespace ChartHub.Server.Services;

internal static class DeviceNameNormalizer
{
    private const int MaxLength = 64;
    private const string FallbackDeviceName = "unknown-device";

    public static string Normalize(string? rawDeviceName)
    {
        string normalized = NormalizeCore(rawDeviceName);
        if (normalized.Length > 0)
        {
            return normalized;
        }

        string fallbackNormalized = NormalizeCore(FallbackDeviceName);
        return fallbackNormalized.Length > 0
            ? fallbackNormalized
            : FallbackDeviceName;
    }

    private static string NormalizeCore(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
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

            if (sb.Length >= MaxLength)
            {
                break;
            }
        }

        return sb.ToString().Trim();
    }
}