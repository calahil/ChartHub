using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ChartHub.Services;

public interface ISyncAdvertisedUrlOptionsProvider
{
    IReadOnlyList<string> GetAdvertisedUrlOptions(string? listenPrefix, string? currentAdvertisedBaseUrl);
}

public sealed class SyncAdvertisedUrlOptionsProvider : ISyncAdvertisedUrlOptionsProvider
{
    private const string DefaultListenPrefix = "http://127.0.0.1:15123/";

    public IReadOnlyList<string> GetAdvertisedUrlOptions(string? listenPrefix, string? currentAdvertisedBaseUrl)
    {
        Uri listenerUri = TryParseListenPrefix(listenPrefix) ?? new Uri(DefaultListenPrefix, UriKind.Absolute);

        var options = new List<string>();
        string autoDetected = SyncApiAddressResolver.ResolveAdvertisedApiBaseUrl(listenerUri.AbsoluteUri, string.Empty);
        if (!string.IsNullOrWhiteSpace(autoDetected))
        {
            options.Add(autoDetected);
        }

        foreach (IPAddress address in GetLanCandidateAddresses())
        {
            UriBuilder builder = new(listenerUri.Scheme, address.ToString(), listenerUri.Port);
            string candidate = builder.Uri.GetLeftPart(UriPartial.Authority);
            if (!options.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                options.Add(candidate);
            }
        }

        string current = NormalizeAdvertisedBaseUrl(currentAdvertisedBaseUrl);
        if (!string.IsNullOrWhiteSpace(current)
            && !options.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            options.Add(current);
        }

        return options;
    }

    private static Uri? TryParseListenPrefix(string? listenPrefix)
    {
        string candidate = listenPrefix?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = $"http://{candidate}";
        }

        if (!candidate.EndsWith("/", StringComparison.Ordinal))
        {
            candidate = $"{candidate}/";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        return uri;
    }

    private static IEnumerable<IPAddress> GetLanCandidateAddresses()
    {
        IEnumerable<IPAddress> addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .Where(network => network.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Where(address => !IPAddress.IsLoopback(address))
            .Where(address => !IsLinkLocalIpv4(address))
            .Distinct()
            .OrderBy(GetAddressPriority)
            .ThenBy(address => address.ToString(), StringComparer.Ordinal);

        return addresses;
    }

    private static int GetAddressPriority(IPAddress address)
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

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return string.Empty;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }
}