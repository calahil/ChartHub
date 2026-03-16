using RhythmVerseClient.Utilities;

namespace RhythmVerseClient.Tests;

[Trait(RhythmVerseClient.Tests.TestInfrastructure.TestCategories.Category, RhythmVerseClient.Tests.TestInfrastructure.TestCategories.IntegrationLite)]
public class LoggerRedactionTests
{
    private static readonly object LoggerSync = new();

    [Fact]
    public void LogInfo_RedactsSensitiveContextAndUriQuery()
    {
        lock (LoggerSync)
        {
            var logDir = CreateTempLogDirectory();
            try
            {
                Logger.Initialize(logDir);
                Logger.LogInfo("Test", "Context redaction check", new Dictionary<string, object?>
                {
                    ["sourceUrl"] = "https://example.com/download?access_token=abc123&foo=bar",
                    ["authorization"] = "Bearer very-secret-token",
                    ["token"] = "abc123",
                });
                Logger.Shutdown();

                var text = ReadLog(logDir);
                Assert.Contains("sourceUrl=https://example.com/download", text, StringComparison.Ordinal);
                Assert.Contains("authorization=[REDACTED]", text, StringComparison.Ordinal);
                Assert.Contains("token=[REDACTED]", text, StringComparison.Ordinal);
                Assert.DoesNotContain("access_token=abc123", text, StringComparison.Ordinal);
                Assert.DoesNotContain("very-secret-token", text, StringComparison.Ordinal);
            }
            finally
            {
                SafeDeleteDirectory(logDir);
            }
        }
    }

    [Fact]
    public void LogWarning_RedactsSensitiveValuesInMessageText()
    {
        lock (LoggerSync)
        {
            var logDir = CreateTempLogDirectory();
            try
            {
                Logger.Initialize(logDir);
                Logger.LogWarning("Test", "request failed url=https://x.test/p?code=ABC123&z=1 authorization=Bearer SUPERSECRET");
                Logger.Shutdown();

                var text = ReadLog(logDir);
                Assert.Contains("[REDACTED]", text, StringComparison.Ordinal);
                Assert.DoesNotContain("ABC123", text, StringComparison.Ordinal);
                Assert.DoesNotContain("SUPERSECRET", text, StringComparison.Ordinal);
            }
            finally
            {
                SafeDeleteDirectory(logDir);
            }
        }
    }

    [Fact]
    public void LogError_RedactsSensitiveValuesInExceptionText()
    {
        lock (LoggerSync)
        {
            var logDir = CreateTempLogDirectory();
            try
            {
                Logger.Initialize(logDir);

                try
                {
                    throw new InvalidOperationException("refresh_token=rt-123 authorization=Bearer TOKEN-XYZ");
                }
                catch (Exception ex)
                {
                    Logger.LogError("Test", "Exception redaction check", ex);
                }

                Logger.Shutdown();

                var text = ReadLog(logDir);
                Assert.Contains("refresh_token=[REDACTED]", text, StringComparison.Ordinal);
                Assert.Contains("[REDACTED]", text, StringComparison.Ordinal);
                Assert.DoesNotContain("rt-123", text, StringComparison.Ordinal);
                Assert.DoesNotContain("TOKEN-XYZ", text, StringComparison.Ordinal);
            }
            finally
            {
                SafeDeleteDirectory(logDir);
            }
        }
    }

    private static string CreateTempLogDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "rv-logger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ReadLog(string logDir)
    {
        var logPath = Path.Combine(logDir, "rhythmverseclient.log");
        Assert.True(File.Exists(logPath), "Expected logger output file to exist.");
        return File.ReadAllText(logPath);
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp test directories.
        }
    }
}
