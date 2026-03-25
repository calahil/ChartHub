using Microsoft.EntityFrameworkCore;

namespace ChartHub.BackupApi.Persistence;

public sealed class BackupDbContext(DbContextOptions<BackupDbContext> options) : DbContext(options)
{
    public DbSet<SongSnapshotEntity> SongSnapshots => Set<SongSnapshotEntity>();

    public DbSet<SyncStateEntity> SyncStates => Set<SyncStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SongSnapshotEntity>(entity =>
        {
            entity.HasIndex(x => x.RecordId).IsUnique();
            entity.HasIndex(x => x.FileId);
            entity.HasIndex(x => x.RecordUpdatedUnix);
            entity.HasIndex(x => x.Artist);
            entity.HasIndex(x => x.Title);
            entity.HasIndex(x => x.Genre);
            entity.HasIndex(x => x.AuthorId);
            entity.HasIndex(x => x.GroupId);
            entity.HasIndex(x => x.GameFormat);
            entity.HasIndex(x => x.DiffGuitar);
            entity.HasIndex(x => x.DiffDrums);
            entity.HasIndex(x => x.LastSyncedUtc);
        });

        modelBuilder.Entity<SyncStateEntity>(entity =>
        {
            entity.HasIndex(x => x.UpdatedUtc);
        });
    }
}
