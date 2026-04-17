using System.Net;

namespace ChartHub.BackupApi.Services;

/// <summary>
/// Abstracts DNS resolution to allow injection of test doubles.
/// </summary>
public interface IDnsResolver
{
    Task<IPAddress[]> GetHostAddressesAsync(string hostNameOrAddress, CancellationToken cancellationToken);
}

/// <summary>
/// Production DNS resolver backed by <see cref="System.Net.Dns"/>.
/// </summary>
public sealed class SystemDnsResolver : IDnsResolver
{
    public static readonly SystemDnsResolver Instance = new();

    public Task<IPAddress[]> GetHostAddressesAsync(string hostNameOrAddress, CancellationToken cancellationToken)
        => Dns.GetHostAddressesAsync(hostNameOrAddress, cancellationToken);
}
