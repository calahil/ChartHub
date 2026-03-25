using ChartHub.BackupApi.Persistence;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ChartHub.BackupApi.Tests.TestInfrastructure;

/// <summary>
/// Wraps a SQLite :memory: connection and a <see cref="BackupDbContext"/> seeded
/// with the EF schema via <c>EnsureCreated()</c>. Each instance owns its own
/// in-memory database that is destroyed when disposed.
/// </summary>
public sealed class SqliteTestContext : IDisposable
{
    private readonly SqliteConnection _connection;

    public BackupDbContext DbContext { get; }

    public SqliteTestContext()
    {
        // Keep the connection open for the lifetime of the context so the
        // in-memory database survives across multiple DbContext operations.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<BackupDbContext> options = new DbContextOptionsBuilder<BackupDbContext>()
            .UseSqlite(_connection)
            .Options;

        DbContext = new BackupDbContext(options);
        DbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        DbContext.Dispose();
        _connection.Dispose();
    }
}
