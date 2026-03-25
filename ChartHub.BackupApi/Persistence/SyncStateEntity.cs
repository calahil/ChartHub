using System.ComponentModel.DataAnnotations;

namespace ChartHub.BackupApi.Persistence;

public sealed class SyncStateEntity
{
    [Key]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedUtc { get; set; }
}
