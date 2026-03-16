namespace ChartHub.Configuration.Interfaces;

public interface ISecretStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ContainsAsync(string key, CancellationToken cancellationToken = default);
}
