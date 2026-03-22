using ChartHub.Configuration.Interfaces;
using ChartHub.Services;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class GoogleAuthTokenDataStoreTests
{
    [Fact]
    public async Task StoreAndGetAsync_RoundTripPayload()
    {
        var secrets = new InMemorySecretStore();
        var sut = new GoogleAuthTokenDataStore(secrets, "google-oauth-test");
        var payload = new TokenLikePayload("access-1", "refresh-1", 3600);

        await sut.StoreAsync("user", payload);
        TokenLikePayload loaded = await sut.GetAsync<TokenLikePayload>("user");

        Assert.NotNull(loaded);
        Assert.Equal("access-1", loaded.AccessToken);
        Assert.Equal("refresh-1", loaded.RefreshToken);
        Assert.Equal(3600, loaded.ExpiresIn);
        Assert.True(await secrets.ContainsAsync("google-oauth-test:user"));
    }

    [Fact]
    public async Task DeleteAsync_RemovesStoredKeyAndUpdatesRegistry()
    {
        var secrets = new InMemorySecretStore();
        var sut = new GoogleAuthTokenDataStore(secrets, "google-oauth-test");

        await sut.StoreAsync("user", new TokenLikePayload("access-1", "refresh-1", 3600));
        await sut.StoreAsync("user-2", new TokenLikePayload("access-2", "refresh-2", 3600));

        await sut.DeleteAsync<TokenLikePayload>("user");

        TokenLikePayload missing = await sut.GetAsync<TokenLikePayload>("user");
        TokenLikePayload stillThere = await sut.GetAsync<TokenLikePayload>("user-2");

        Assert.Null(missing);
        Assert.NotNull(stillThere);
        Assert.False(await secrets.ContainsAsync("google-oauth-test:user"));
        Assert.True(await secrets.ContainsAsync("google-oauth-test:user-2"));
        Assert.True(await secrets.ContainsAsync("google-oauth-test:__keys"));
    }

    [Fact]
    public async Task ClearAsync_RemovesAllStoredKeysAndRegistry()
    {
        var secrets = new InMemorySecretStore();
        var sut = new GoogleAuthTokenDataStore(secrets, "google-oauth-test");

        await sut.StoreAsync("user", new TokenLikePayload("access-1", "refresh-1", 3600));
        await sut.StoreAsync("user-2", new TokenLikePayload("access-2", "refresh-2", 3600));

        await sut.ClearAsync();

        TokenLikePayload user = await sut.GetAsync<TokenLikePayload>("user");
        TokenLikePayload user2 = await sut.GetAsync<TokenLikePayload>("user-2");

        Assert.Null(user);
        Assert.Null(user2);
        Assert.False(await secrets.ContainsAsync("google-oauth-test:user"));
        Assert.False(await secrets.ContainsAsync("google-oauth-test:user-2"));
        Assert.False(await secrets.ContainsAsync("google-oauth-test:__keys"));
    }

    [Fact]
    public async Task StoreAsync_DoesNotDuplicateRegistryEntries()
    {
        var secrets = new InMemorySecretStore();
        var sut = new GoogleAuthTokenDataStore(secrets, "google-oauth-test");

        await sut.StoreAsync("user", new TokenLikePayload("access-1", "refresh-1", 3600));
        await sut.StoreAsync("user", new TokenLikePayload("access-2", "refresh-2", 1800));

        string? registry = await secrets.GetAsync("google-oauth-test:__keys");

        Assert.NotNull(registry);
        HashSet<string>? parsed = System.Text.Json.JsonSerializer.Deserialize<HashSet<string>>(registry!);
        Assert.NotNull(parsed);
        Assert.Single(parsed!);
        Assert.Contains("google-oauth-test:user", parsed!);
    }

    private sealed record TokenLikePayload(string AccessToken, string RefreshToken, int ExpiresIn);

    private sealed class InMemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue(key, out string? value);
            return Task.FromResult<string?>(value);
        }

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ContainsAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_store.ContainsKey(key));
        }
    }
}
