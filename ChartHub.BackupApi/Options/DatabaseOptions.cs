namespace ChartHub.BackupApi.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = "postgresql";

    public string PostgreSqlConnectionString { get; set; } = string.Empty;

    public string SqliteConnectionString { get; set; } = "Data Source=charthub-backup.db";
}
