namespace ChartHub.BackupApi.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = "postgresql";

    public string PostgreSqlConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=charthub_backup;Username=postgres;Password=postgres";

    public string SqliteConnectionString { get; set; } = "Data Source=charthub-backup.db";
}
