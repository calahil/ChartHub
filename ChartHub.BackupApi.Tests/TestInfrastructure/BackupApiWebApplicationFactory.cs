using ChartHub.BackupApi.Persistence;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChartHub.BackupApi.Tests.TestInfrastructure;

public static class WebApplicationFactoryExtensions
{
    public static HttpClient CreateAuthenticatedClient(this Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", BackupApiWebApplicationFactory.TestApiKey);
        return client;
    }
}

public sealed class BackupApiWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    public const string TestApiKey = "test-api-key-for-integration-tests";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"charthub-backupapi-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(
            (_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("Database:Provider", "sqlite"),
                    new KeyValuePair<string, string?>("Database:SqliteConnectionString", $"Data Source={_databasePath}"),
                    new KeyValuePair<string, string?>("Sync:Enabled", "false"),
                    new KeyValuePair<string, string?>("ApiKey:Key", TestApiKey),
                ]);
            });

        builder.ConfigureServices(services =>
        {
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            using IServiceScope scope = serviceProvider.CreateScope();

            BackupDbContext dbContext = scope.ServiceProvider.GetRequiredService<BackupDbContext>();
            dbContext.Database.EnsureDeleted();
            dbContext.Database.EnsureCreated();
        });
    }

    public async Task SeedSongsAsync(
        IReadOnlyList<SongSnapshotEntity> songs,
        bool clearExisting = true,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = Services.CreateScope();
        BackupDbContext dbContext = scope.ServiceProvider.GetRequiredService<BackupDbContext>();

        if (clearExisting)
        {
            await dbContext.SongSnapshots.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        dbContext.SongSnapshots.AddRange(songs);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SeedSyncStatesAsync(
        IReadOnlyList<KeyValuePair<string, string>> entries,
        bool clearExisting = true,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = Services.CreateScope();
        BackupDbContext dbContext = scope.ServiceProvider.GetRequiredService<BackupDbContext>();

        if (clearExisting)
        {
            dbContext.SyncStates.RemoveRange(dbContext.SyncStates);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (KeyValuePair<string, string> entry in entries)
        {
            SyncStateEntity? existing = await dbContext.SyncStates
                .FirstOrDefaultAsync(x => x.Key == entry.Key, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                existing = new SyncStateEntity
                {
                    Key = entry.Key,
                };

                dbContext.SyncStates.Add(existing);
            }

            existing.Value = entry.Value;
            existing.UpdatedUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public new void Dispose()
    {
        base.Dispose();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}