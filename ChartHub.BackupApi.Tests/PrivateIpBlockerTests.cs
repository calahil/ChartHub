using System.Net;

using ChartHub.BackupApi.Services;
using ChartHub.BackupApi.Tests.TestInfrastructure;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class PrivateIpBlockerTests
{
    [Theory]
    [InlineData("127.0.0.1")]           // Loopback
    [InlineData("127.255.255.255")]     // Loopback end
    [InlineData("10.0.0.1")]            // RFC 1918: 10.x
    [InlineData("10.255.255.255")]      // RFC 1918: 10.x end
    [InlineData("172.16.0.1")]          // RFC 1918: 172.16
    [InlineData("172.31.255.255")]      // RFC 1918: 172.31 end
    [InlineData("172.16.100.50")]       // RFC 1918: 172.16 middle
    [InlineData("192.168.0.1")]         // RFC 1918: 192.168
    [InlineData("192.168.255.255")]     // RFC 1918: 192.168 end
    [InlineData("169.254.169.254")]     // Cloud metadata (AWS/Azure/GCP)
    [InlineData("169.254.0.1")]         // Link-local start
    [InlineData("169.254.255.255")]     // Link-local end
    [InlineData("224.0.0.1")]           // Multicast start
    [InlineData("239.255.255.255")]     // Multicast end
    [InlineData("255.255.255.255")]     // Broadcast
    [InlineData("::1")]                 // IPv6 loopback
    [InlineData("fc00::1")]             // IPv6 unique-local
    [InlineData("fd00::1")]             // IPv6 unique-local (fd sub-range)
    [InlineData("fe80::1")]             // IPv6 link-local
    [InlineData("ff00::1")]             // IPv6 multicast
    [InlineData("::ffff:10.0.0.1")]     // IPv4-mapped RFC 1918
    [InlineData("::ffff:192.168.1.1")]  // IPv4-mapped private
    [InlineData("::ffff:169.254.169.254")] // IPv4-mapped cloud metadata
    public void IsPrivateOrReserved_ReturnsTrue_ForPrivateAndReservedAddresses(string ipString)
    {
        var address = IPAddress.Parse(ipString);

        bool result = PrivateIpBlocker.IsPrivateOrReserved(address);

        Assert.True(result);
    }

    [Theory]
    [InlineData("8.8.8.8")]             // Google Public DNS
    [InlineData("1.1.1.1")]             // Cloudflare DNS
    [InlineData("172.15.255.255")]      // Just below RFC 1918 172.16 range
    [InlineData("172.32.0.1")]          // Just above RFC 1918 172.16–31 range
    [InlineData("2001:db8::1")]         // IPv6 documentation address
    [InlineData("2606:4700:4700::1111")] // Cloudflare IPv6 DNS
    public void IsPrivateOrReserved_ReturnsFalse_ForPublicAddresses(string ipString)
    {
        var address = IPAddress.Parse(ipString);

        bool result = PrivateIpBlocker.IsPrivateOrReserved(address);

        Assert.False(result);
    }
}
