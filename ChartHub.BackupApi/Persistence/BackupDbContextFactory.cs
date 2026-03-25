using ChartHub.BackupApi.Options;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChartHub.BackupApi.Persistence;

/// <summary>
/// Used by dotnet-ef CLI tooling at design time.
/// Always uses SQLite so no live database is required to generate migrations.
/// </summary>
public sealed class BackupDbContextFactory : IDesignTimeDbContextFactory<BackupDbContext>
{
    public BackupDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<BackupDbContext> optionsBuilder = new();
        optionsBuilder.UseSqlite(new DatabaseOptions().SqliteConnectionString);
        return new BackupDbContext(optionsBuilder.Options);
    }
}
