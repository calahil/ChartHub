using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ChartHub.Services;

public static class SyncApiAddressResolver
{
    private const string DefaultListenPrefix = "http://127.0.0.1:15123/";

    public static IReadOnlyList<string> ResolveAdvertisedApiBaseUrls(string? listenPrefix, string? advertisedBaseUrl)
    {
        string normalizedAdvertisedBaseUrl = NormalizeAdvertisedBaseUrl(advertisedBaseUrl);
        if (Uri.TryCreate(normalizedAdvertisedBaseUrl, UriKind.Absolute, out Uri? advertisedUri)
            && !string.IsNullOrWhiteSpace(advertisedUri.Host))
        {
            return [advertisedUri.GetLeftPart(UriPartial.Authority)];
        }

        if (!Uri.TryCreate(listenPrefix, UriKind.Absolute, out Uri? parsedListener)
            || string.IsNullOrWhiteSpace(parsedListener.Host))
        {
            return [DefaultListenPrefix.TrimEnd('/')];
        }

        if (!IsWildcardHost(parsedListener.Host))
        {
            return [parsedListener.GetLeftPart(UriPartial.Authority)];
        }

        var candidates = GetLanCandidateAddresses()
            .OrderBy(address => GetLanAddressPreference(address))
            .ThenBy(address => address.ToString(), StringComparer.Ordinal)
            .Select(address => new UriBuilder(parsedListener.Scheme, address.ToString(), parsedListener.Port)
                .Uri
                .GetLeftPart(UriPartial.Authority))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            return [DefaultListenPrefix.TrimEnd('/')];
        }

        return candidates;
    }

    public static string ResolveAdvertisedApiBaseUrl(string? listenPrefix, string? advertisedBaseUrl)
    {
        return ResolveAdvertisedApiBaseUrls(listenPrefix, advertisedBaseUrl).First();
    }

    private static string NormalizeAdvertisedBaseUrl(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = $"http://{candidate}";
        }

        return candidate.EndsWith("/", StringComparison.Ordinal)
            ? candidate[..^1]
            : candidate;
    }

    private static IEnumerable<IPAddress> GetLanCandidateAddresses()
    {
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces()
                     .OrderBy(candidate => candidate.Name, StringComparer.Ordinal))
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            IPInterfaceProperties properties = networkInterface.GetIPProperties();
            foreach (UnicastIPAddressInformation addressInformation in properties.UnicastAddresses)
            {
                IPAddress address = addressInformation.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork
                    || IPAddress.IsLoopback(address)
                    || IsLinkLocalIpv4(address))
                {
                    continue;
                }

                yield return address;
            }
        }
    }

    private static int GetLanAddressPreference(IPAddress address)
    {
        return IsPrivateLanIpv4(address) ? 0 : 1;
    }

    private static bool IsPrivateLanIpv4(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    private static bool IsLinkLocalIpv4(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    private static bool IsWildcardHost(string host)
    {
        return string.Equals(host, "+", StringComparison.Ordinal)
            || string.Equals(host, "*", StringComparison.Ordinal)
            || string.Equals(host, "0.0.0.0", StringComparison.Ordinal)
            || string.Equals(host, "::", StringComparison.Ordinal);
    }
}