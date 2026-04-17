using System.Net;
using System.Net.Sockets;

namespace ChartHub.BackupApi.Services;

/// <summary>
/// Guards against SSRF by detecting private, link-local, loopback, and otherwise reserved IP addresses.
/// All address families outside IPv4 and IPv6 are treated as blocked.
/// </summary>
public static class PrivateIpBlocker
{
    /// <summary>
    /// Returns <see langword="true"/> if the address is private, reserved, or otherwise
    /// unsafe to use as an outbound proxy target.
    /// </summary>
    public static bool IsPrivateOrReserved(IPAddress address)
    {
        // Unwrap IPv4-mapped IPv6 addresses (e.g. ::ffff:10.0.0.1).
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPrivateIpv4(address),
            AddressFamily.InterNetworkV6 => IsPrivateIpv6(address),
            _ => true, // Reject unknown address families.
        };
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        return
            b[0] == 127                                     // Loopback: 127.0.0.0/8
            || b[0] == 10                                   // RFC 1918: 10.0.0.0/8
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)   // RFC 1918: 172.16.0.0/12
            || (b[0] == 192 && b[1] == 168)                 // RFC 1918: 192.168.0.0/16
            || (b[0] == 169 && b[1] == 254)                 // Link-local: 169.254.0.0/16 (cloud metadata)
            || (b[0] >= 224 && b[0] <= 239)                 // Multicast: 224.0.0.0/4
            || (b[0] == 255 && b[1] == 255 && b[2] == 255 && b[3] == 255); // Broadcast
    }

    private static bool IsPrivateIpv6(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        byte[] b = address.GetAddressBytes();
        return
            (b[0] & 0xFE) == 0xFC                          // Unique-local: fc00::/7
            || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80)     // Link-local: fe80::/10
            || b[0] == 0xFF;                                // Multicast: ff00::/8
    }
}
