using System.Collections.Concurrent;
using System.Net;

using Makaretu.Dns;

namespace ChartHub.Services;

public interface ISyncLanDiscoveryService
{
    Task<IReadOnlyList<SyncDiscoveryEndpoint>> DiscoverAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    Task StartAdvertisingAsync(string baseUrl, string deviceLabel, CancellationToken cancellationToken = default);
    Task StopAdvertisingAsync(CancellationToken cancellationToken = default);
}

public sealed record SyncDiscoveryEndpoint(
    string ServiceInstanceName,
    string BaseUrl,
    string DeviceLabel,
    DateTimeOffset DiscoveredAtUtc)
{
    public string DisplayLabel => string.IsNullOrWhiteSpace(DeviceLabel)
        ? BaseUrl
        : $"{DeviceLabel} ({BaseUrl})";
}

public sealed class SyncLanDiscoveryService : ISyncLanDiscoveryService, IAsyncDisposable
{
    private const string ServiceType = "_charthub-sync._tcp";
    private const string ApiContractName = "ingestion-sync";
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _advertiseGate = new(1, 1);

    private ServiceDiscovery? _advertiser;
    private ServiceProfile? _advertisedProfile;

    public async Task<IReadOnlyList<SyncDiscoveryEndpoint>> DiscoverAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        TimeSpan effectiveTimeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(3) : timeout;
        var discovered = new ConcurrentDictionary<string, SyncDiscoveryEndpoint>(StringComparer.OrdinalIgnoreCase);
        var resolveTasks = new ConcurrentBag<Task>();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(effectiveTimeout);

        using var browser = new ServiceDiscovery();
        browser.ServiceInstanceDiscovered += (_, args) =>
        {
            Task resolveTask = ResolveServiceInstanceAsync(args.ServiceInstanceName, discovered, linkedCts.Token);
            resolveTasks.Add(resolveTask);
        };

        browser.QueryServiceInstances(ServiceType);

        try
        {
            await Task.Delay(effectiveTimeout, linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Expected when timeout elapses.
        }

        Task[] pending = resolveTasks.ToArray();
        if (pending.Length > 0)
        {
            try
            {
                await Task.WhenAll(pending);
            }
            catch (OperationCanceledException)
            {
                // Ignore late resolver completion after timeout.
            }
        }

        return discovered
            .Values
            .OrderBy(entry => entry.DeviceLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.BaseUrl, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task StartAdvertisingAsync(string baseUrl, string deviceLabel, CancellationToken cancellationToken = default)
    {
        await _advertiseGate.WaitAsync(cancellationToken);
        try
        {
            await StopAdvertisingCoreAsync(cancellationToken);

            if (!TryCreateServiceProfile(baseUrl, deviceLabel, out ServiceProfile? profile)
                || profile is null)
            {
                return;
            }

            var advertiser = new ServiceDiscovery();
            advertiser.Advertise(profile);
            advertiser.Announce(profile);

            _advertiser = advertiser;
            _advertisedProfile = profile;
        }
        finally
        {
            _advertiseGate.Release();
        }
    }

    public async Task StopAdvertisingAsync(CancellationToken cancellationToken = default)
    {
        await _advertiseGate.WaitAsync(cancellationToken);
        try
        {
            await StopAdvertisingCoreAsync(cancellationToken);
        }
        finally
        {
            _advertiseGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAdvertisingAsync();
        _advertiseGate.Dispose();
    }

    private async Task ResolveServiceInstanceAsync(
        DomainName serviceInstanceName,
        ConcurrentDictionary<string, SyncDiscoveryEndpoint> discovered,
        CancellationToken cancellationToken)
    {
        try
        {
            using var resolveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            resolveCts.CancelAfter(ResolveTimeout);

            using var multicast = new MulticastService();
            multicast.Start();

            Message query = new();
            query.Questions.Add(new Question
            {
                Name = serviceInstanceName,
                Type = DnsType.ANY,
            });

            Message response = await multicast.ResolveAsync(query, resolveCts.Token);
            var records = response.Answers
                .Concat(response.AdditionalRecords)
                .ToList();

            SRVRecord? srv = records
                .OfType<SRVRecord>()
                .FirstOrDefault(record => string.Equals(
                    record.Name.ToString(),
                    serviceInstanceName.ToString(),
                    StringComparison.OrdinalIgnoreCase));
            if (srv is null)
            {
                return;
            }

            TXTRecord? txt = records
                .OfType<TXTRecord>()
                .FirstOrDefault(record => string.Equals(
                    record.Name.ToString(),
                    serviceInstanceName.ToString(),
                    StringComparison.OrdinalIgnoreCase));

            Dictionary<string, string> metadata = ParseTxtMetadata(txt?.Strings ?? []);
            if (metadata.TryGetValue("api", out string? advertisedApi)
                && !string.Equals(advertisedApi, ApiContractName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string baseUrl = ResolveBaseUrl(metadata, srv);
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? parsedBase)
                || string.IsNullOrWhiteSpace(parsedBase.Host)
                || IsLoopbackHost(parsedBase.Host))
            {
                return;
            }

            string discoveredLabel = metadata.TryGetValue("label", out string? labelValue)
                ? labelValue
                : ExtractInstanceLabel(serviceInstanceName.ToString());

            var endpoint = new SyncDiscoveryEndpoint(
                ServiceInstanceName: serviceInstanceName.ToString().TrimEnd('.'),
                BaseUrl: parsedBase.GetLeftPart(UriPartial.Authority),
                DeviceLabel: discoveredLabel,
                DiscoveredAtUtc: DateTimeOffset.UtcNow);

            discovered[endpoint.BaseUrl] = endpoint;
        }
        catch (OperationCanceledException)
        {
            // Timeout/cancelled discovery is expected.
        }
        catch
        {
            // Discovery runs best-effort and should never break caller flows.
        }
    }

    private async Task StopAdvertisingCoreAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        if (_advertiser is not null)
        {
            if (_advertisedProfile is not null)
            {
                _advertiser.Unadvertise(_advertisedProfile);
            }

            _advertiser.Dispose();
        }

        _advertiser = null;
        _advertisedProfile = null;
    }

    private static bool TryCreateServiceProfile(string baseUrl, string deviceLabel, out ServiceProfile? profile)
    {
        profile = null;

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out Uri? parsed)
            || string.IsNullOrWhiteSpace(parsed.Host)
            || parsed.Port <= 0
            || IsLoopbackHost(parsed.Host))
        {
            return false;
        }

        IReadOnlyList<IPAddress> addresses = ResolveAdvertisedAddresses(parsed.Host);
        if (addresses.Count == 0)
        {
            return false;
        }

        string advertisedBaseUrl = BuildAdvertisedBaseUrl(parsed, addresses[0]);
        if (string.IsNullOrWhiteSpace(advertisedBaseUrl))
        {
            return false;
        }

        string instanceName = ToDnsLabel(string.IsNullOrWhiteSpace(deviceLabel) ? "ChartHub Desktop" : deviceLabel);
        var serviceProfile = new ServiceProfile(
            new DomainName(instanceName),
            new DomainName(ServiceType),
            (ushort)parsed.Port,
            addresses);

        serviceProfile.AddProperty("api", ApiContractName);
        serviceProfile.AddProperty("base", advertisedBaseUrl);
        serviceProfile.AddProperty("label", string.IsNullOrWhiteSpace(deviceLabel) ? "ChartHub Desktop" : deviceLabel.Trim());

        profile = serviceProfile;
        return true;
    }

    private static IReadOnlyList<IPAddress> ResolveAdvertisedAddresses(string host)
    {
        if (IPAddress.TryParse(host, out IPAddress? parsed)
            && !IPAddress.IsLoopback(parsed)
            && parsed.ToString() is not "0.0.0.0" and not "::")
        {
            return [parsed];
        }

        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(host);
            IPAddress[] routable = addresses
                .Where(address => address.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork or System.Net.Sockets.AddressFamily.InterNetworkV6)
                .Where(address => !IPAddress.IsLoopback(address))
                .ToArray();
            if (routable.Length > 0)
            {
                return routable;
            }
        }
        catch
        {
            // Ignore DNS resolution failures and fall back to local interfaces.
        }

        return MulticastService.GetIPAddresses()
            .Where(address => !IPAddress.IsLoopback(address))
            .Where(address => address.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork or System.Net.Sockets.AddressFamily.InterNetworkV6)
            .ToArray();
    }

    private static string BuildAdvertisedBaseUrl(Uri parsedListenUri, IPAddress advertisedAddress)
    {
        string host = IsWildcardHost(parsedListenUri.Host) ? advertisedAddress.ToString() : parsedListenUri.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        var builder = new UriBuilder(parsedListenUri.Scheme, host, parsedListenUri.Port);
        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }

    private static Dictionary<string, string> ParseTxtMetadata(IEnumerable<string> entries)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in entries)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            int separator = raw.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string key = raw[..separator].Trim();
            string value = raw[(separator + 1)..].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            metadata[key] = value;
        }

        return metadata;
    }

    private static string ResolveBaseUrl(IReadOnlyDictionary<string, string> metadata, SRVRecord srvRecord)
    {
        if (metadata.TryGetValue("base", out string? configuredBase)
            && Uri.TryCreate(configuredBase, UriKind.Absolute, out Uri? parsedConfigured)
            && !string.IsNullOrWhiteSpace(parsedConfigured.Host))
        {
            return parsedConfigured.GetLeftPart(UriPartial.Authority);
        }

        string host = srvRecord.Target.ToString().TrimEnd('.');
        var builder = new UriBuilder(Uri.UriSchemeHttp, host, srvRecord.Port);
        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }

    private static string ExtractInstanceLabel(string serviceInstanceName)
    {
        if (string.IsNullOrWhiteSpace(serviceInstanceName))
        {
            return "ChartHub Desktop";
        }

        string trimmed = serviceInstanceName.TrimEnd('.');
        int delimiter = trimmed.IndexOf("._", StringComparison.Ordinal);
        string instance = delimiter > 0 ? trimmed[..delimiter] : trimmed;
        return instance.Replace('-', ' ').Trim();
    }

    private static string ToDnsLabel(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return "charthub-desktop";
        }

        Span<char> buffer = stackalloc char[Math.Min(trimmed.Length, 40)];
        int index = 0;
        foreach (char c in trimmed)
        {
            if (index >= buffer.Length)
            {
                break;
            }

            if (char.IsLetterOrDigit(c))
            {
                buffer[index++] = char.ToLowerInvariant(c);
            }
            else if (index > 0 && buffer[index - 1] != '-')
            {
                buffer[index++] = '-';
            }
        }

        string label = new string(buffer[..index]).Trim('-');
        return label.Length == 0 ? "charthub-desktop" : label;
    }

    private static bool IsWildcardHost(string host)
    {
        return host.Equals("*", StringComparison.Ordinal)
            || host.Equals("+", StringComparison.Ordinal)
            || host.Equals("0.0.0.0", StringComparison.Ordinal)
            || host.Equals("::", StringComparison.Ordinal);
    }

    private static bool IsLoopbackHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address));
    }
}
