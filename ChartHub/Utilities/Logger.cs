using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ChartHub.Utilities;

public enum AppLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Critical,
}

public static class Logger
{
    private static readonly object Sync = new();
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token",
        "access_token",
        "refresh_token",
        "client_secret",
        "secret",
        "password",
        "authorization",
        "cookie",
    };

    private static readonly Regex SensitiveAssignmentPattern = new(
        "(?i)(access_token|refresh_token|client_secret|authorization|password|cookie)(\\s*[=:]\\s*)([^&;\\r\\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerTokenPattern = new(
        "(?i)(bearer\\s+)([^\\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveQueryPattern = new(
        "(?i)([?&](?:access_token|refresh_token|token|code|client_secret)=)([^&#\\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const long MaxLogFileBytes = 1_048_576;

    private static string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ChartHub",
        "logs");
    private static string _logFilePath = Path.Combine(_logDirectory, "charthub.log");
    private static string _sessionId = CreateSessionId();
    private static bool _initialized;
    private static bool _writeFailureReported;

    public static string CurrentSessionId
    {
        get
        {
            lock (Sync)
            {
                return _sessionId;
            }
        }
    }

    public static void Initialize(string? logDirectory = null, IReadOnlyDictionary<string, object?>? context = null)
    {
        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                _logDirectory = logDirectory;
                _logFilePath = Path.Combine(_logDirectory, "charthub.log");
            }

            Directory.CreateDirectory(_logDirectory);
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _writeFailureReported = false;

            Dictionary<string, object?> sessionContext = BuildSessionContext();
            MergeContext(sessionContext, context);
            WriteEntryUnsafe(AppLogLevel.Info, "App", "Session started", null, sessionContext);
        }
    }

    public static void Shutdown(IReadOnlyDictionary<string, object?>? context = null)
    {
        lock (Sync)
        {
            if (!_initialized)
            {
                return;
            }

            WriteEntryUnsafe(AppLogLevel.Info, "App", "Session ended", null, context);
            _initialized = false;
            _sessionId = CreateSessionId();
        }
    }

    public static void LogDebug(string category, string message, IReadOnlyDictionary<string, object?>? context = null)
        => Log(AppLogLevel.Debug, category, message, null, context);

    public static void LogInfo(string category, string message, IReadOnlyDictionary<string, object?>? context = null)
        => Log(AppLogLevel.Info, category, message, null, context);

    public static void LogWarning(string category, string message, IReadOnlyDictionary<string, object?>? context = null)
        => Log(AppLogLevel.Warning, category, message, null, context);

    public static void LogError(string category, string message, Exception exception, IReadOnlyDictionary<string, object?>? context = null)
        => Log(AppLogLevel.Error, category, message, exception, context);

    public static void LogCritical(string category, string message, Exception exception, IReadOnlyDictionary<string, object?>? context = null)
        => Log(AppLogLevel.Critical, category, message, exception, context);

    public static void LogError(Exception ex)
    {
        Log(AppLogLevel.Error, "App", ex.Message, ex, null);
    }

    public static void LogError(Exception ex, string message)
    {
        Log(AppLogLevel.Error, "App", message, ex, null);
    }

    public static void LogMessage(string message)
    {
        Log(AppLogLevel.Info, "App", message, null, null);
    }

    public static void LogMessage(string category, string message)
    {
        Log(AppLogLevel.Info, category, message, null, null);
    }

    private static void Log(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception,
        IReadOnlyDictionary<string, object?>? context)
    {
        try
        {
            lock (Sync)
            {
                EnsureInitializedUnsafe();
                WriteEntryUnsafe(level, category, message, exception, context);
            }
        }
        catch (Exception writeException)
        {
            ReportWriteFailure(writeException);
        }
    }

    private static void EnsureInitializedUnsafe()
    {
        if (_initialized)
        {
            return;
        }

        Directory.CreateDirectory(_logDirectory);
        _initialized = true;
        _writeFailureReported = false;
        WriteEntryUnsafe(AppLogLevel.Info, "App", "Session started", null, BuildSessionContext());
    }

    private static void WriteEntryUnsafe(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception,
        IReadOnlyDictionary<string, object?>? context)
    {
        RotateIfNeededUnsafe();

        using var writer = new StreamWriter(_logFilePath, append: true, Encoding.UTF8);
        string redactedMessage = RedactSensitiveText(message);
        writer.WriteLine($"[{DateTimeOffset.UtcNow:O}] [{level}] [{SanitizeValue(category)}] {SanitizeValue(redactedMessage)}");
        writer.WriteLine($"sessionId={SanitizeValue(_sessionId)}");

        if (context is not null)
        {
            foreach (KeyValuePair<string, object?> pair in context)
            {
                writer.WriteLine($"{SanitizeValue(pair.Key)}={FormatContextValue(pair.Key, pair.Value)}");
            }
        }

        if (exception is not null)
        {
            writer.WriteLine($"exceptionType={SanitizeValue(exception.GetType().FullName ?? exception.GetType().Name)}");
            writer.WriteLine($"exception={SanitizeMultiline(RedactSensitiveText(exception.ToString()))}");
        }

        writer.WriteLine();
    }

    private static void RotateIfNeededUnsafe()
    {
        Directory.CreateDirectory(_logDirectory);
        if (!File.Exists(_logFilePath))
        {
            return;
        }

        var info = new FileInfo(_logFilePath);
        if (info.Length < MaxLogFileBytes)
        {
            return;
        }

        string archivePath = _logFilePath + ".1";
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        File.Move(_logFilePath, archivePath);
    }

    private static Dictionary<string, object?> BuildSessionContext()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["appVersion"] = version,
            ["processId"] = Environment.ProcessId,
            ["baseDirectory"] = AppContext.BaseDirectory,
            ["osDescription"] = RuntimeInformation.OSDescription,
            ["framework"] = RuntimeInformation.FrameworkDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
        };
    }

    private static void MergeContext(Dictionary<string, object?> target, IReadOnlyDictionary<string, object?>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (KeyValuePair<string, object?> pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static string FormatContextValue(string key, object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (IsSensitiveKey(key))
        {
            return "[REDACTED]";
        }

        string stringValue = value.ToString() ?? string.Empty;
        if (LooksLikeUriKey(key) && TryRedactUri(stringValue, out string? redactedUri))
        {
            return SanitizeValue(redactedUri);
        }

        return SanitizeValue(RedactSensitiveText(stringValue));
    }

    private static bool IsSensitiveKey(string key)
    {
        if (SensitiveKeys.Contains(key))
        {
            return true;
        }

        return SensitiveKeys.Any(sensitive => key.Contains(sensitive, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeUriKey(string key)
    {
        return key.Contains("url", StringComparison.OrdinalIgnoreCase)
            || key.Contains("uri", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryRedactUri(string value, out string redacted)
    {
        redacted = string.Empty;

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.IsFile)
        {
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };

        redacted = builder.Uri.GetLeftPart(UriPartial.Path);
        return true;
    }

    private static string SanitizeValue(string value)
    {
        return value
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string SanitizeMultiline(string value)
    {
        return value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string RedactSensitiveText(string value)
    {
        string redacted = SensitiveAssignmentPattern.Replace(value, "$1$2[REDACTED]");
        redacted = BearerTokenPattern.Replace(redacted, "$1[REDACTED]");
        redacted = SensitiveQueryPattern.Replace(redacted, "$1[REDACTED]");
        return redacted;
    }

    private static string CreateSessionId()
    {
        return $"app-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}";
    }

    private static void ReportWriteFailure(Exception exception)
    {
        if (_writeFailureReported)
        {
            return;
        }

        _writeFailureReported = true;
        Trace.WriteLine($"ChartHub logger write failure: {exception}");
    }
}