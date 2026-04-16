namespace ChartHub.BackupApi.Options;

public sealed class LoggingOptions
{
    public const string SectionName = "Logging:Sink";

    /// <summary>Name of the PostgreSQL table the Serilog sink will write to (and auto-create if absent).</summary>
    public string SinkTableName { get; set; } = "Logs";

    /// <summary>Number of days to retain log rows. Rows older than this are deleted by the retention service.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>Maximum number of log events to flush to PostgreSQL per batch.</summary>
    public int BatchSizeLimit { get; set; } = 50;

    /// <summary>How often (in seconds) the sink flushes a batch to PostgreSQL.</summary>
    public int PeriodSeconds { get; set; } = 5;
}
