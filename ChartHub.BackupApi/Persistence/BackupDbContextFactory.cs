using ChartHub.BackupApi.Options;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChartHub.BackupApi.Persistence;

/// <summary>
/// Used by dotnet-ef CLI tooling at design time.
/// Defaults to PostgreSQL so migrations align with runtime provider.
/// Set CHART_HUB_DB_PROVIDER=sqlite to generate SQLite-specific migrations when needed.
/// </summary>
public sealed class BackupDbContextFactory : IDesignTimeDbContextFactory<BackupDbContext>
{
    public BackupDbContext CreateDbContext(string[] args)
    {
        DatabaseOptions options = new();
        string provider = Environment.GetEnvironmentVariable("CHART_HUB_DB_PROVIDER") ?? "postgresql";

        DbContextOptionsBuilder<BackupDbContext> optionsBuilder = new();

        if (string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            optionsBuilder.UseSqlite(options.SqliteConnectionString);
        }
        else
        {
            optionsBuilder.UseNpgsql(options.PostgreSqlConnectionString);
        }

        return new BackupDbContext(optionsBuilder.Options);
    }
}
