using System.Text;

namespace ChartHub.Server.Services;

public sealed class ServerFileLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly TextWriter _writer;

    public ServerFileLoggerProvider(string logDirectory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("Log directory cannot be null or whitespace.", nameof(logDirectory));
        }

        string resolvedFileName = string.IsNullOrWhiteSpace(fileName)
            ? "charthub-server.log"
            : fileName.Trim();

        Directory.CreateDirectory(logDirectory);
        string filePath = Path.Combine(logDirectory, resolvedFileName);
        _writer = TryOpenLogFile(filePath);
    }

    internal ServerFileLoggerProvider(TextWriter writer)
    {
        _writer = writer;
    }

    private static StreamWriter TryOpenLogFile(string filePath)
    {
        try
        {
            return new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true,
            };
        }
        catch (UnauthorizedAccessException)
        {
            // The existing log file may be owned by a different process user (e.g., after a service user change).
            // Attempt to remove the inaccessible file and start fresh.
            try
            {
                File.Delete(filePath);
                return new StreamWriter(new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    AutoFlush = true,
                };
            }
            catch (Exception)
            {
                // The log directory is not writable by this process.
                // Degrade gracefully; logs remain available via the system journal.
                return StreamWriter.Null;
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new ServerFileLogger(categoryName, this);

    public void Dispose()
    {
        lock (_sync)
        {
            _writer.Dispose();
        }
    }

    private void WriteLog(LogLevel level, EventId eventId, string categoryName, string message, Exception? exception)
    {
        var line = new StringBuilder(256);
        line.Append('[')
            .Append(DateTimeOffset.UtcNow.ToString("O"))
            .Append("] [")
            .Append(level)
            .Append("] [")
            .Append(categoryName)
            .Append(']');

        if (eventId.Id != 0)
        {
            line.Append(" [EventId=").Append(eventId.Id).Append(']');
        }

        line.Append(' ').Append(message);

        if (exception is not null)
        {
            line.AppendLine();
            line.Append(exception);
        }

        lock (_sync)
        {
            _writer.WriteLine(line.ToString());
        }
    }

    private sealed class ServerFileLogger(string categoryName, ServerFileLoggerProvider provider) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);
            string message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            provider.WriteLog(logLevel, eventId, categoryName, message, exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}