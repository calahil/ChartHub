using ChartHub.Utilities;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.IntegrationLite)]
public class LoggerLifecycleTests
{
    private static readonly object LoggerSync = new();

    [Fact]
    public void InitializeAndShutdown_EmitSessionStartAndEndMarkers()
    {
        lock (LoggerSync)
        {
            string logDir = CreateTempLogDirectory();
            try
            {
                Logger.Shutdown();
                Logger.Initialize(logDir, new Dictionary<string, object?>
                {
                    ["startupMode"] = "test",
                });
                Logger.LogInfo("Test", "Lifecycle smoke marker");
                Logger.Shutdown(new Dictionary<string, object?>
                {
                    ["shutdownReason"] = "test",
                });

                string text = ReadLog(logDir);
                Assert.Contains("Session started", text, StringComparison.Ordinal);
                Assert.Contains("Session ended", text, StringComparison.Ordinal);
                Assert.Contains("startupMode=test", text, StringComparison.Ordinal);
                Assert.Contains("shutdownReason=test", text, StringComparison.Ordinal);
                Assert.Contains("sessionId=app-", text, StringComparison.Ordinal);
            }
            finally
            {
                SafeDeleteDirectory(logDir);
            }
        }
    }

    [Fact]
    public void LogInfo_RotatesWhenCurrentLogExceedsMaxSize()
    {
        lock (LoggerSync)
        {
            string logDir = CreateTempLogDirectory();
            try
            {
                Logger.Shutdown();
                string logPath = Path.Combine(logDir, "charthub.log");
                string oversized = new string('X', 1_100_000);
                File.WriteAllText(logPath, $"preexisting-marker\n{oversized}");

                Logger.Initialize(logDir);
                Logger.LogInfo("Test", "Post-rotation marker");
                Logger.Shutdown();

                string archivePath = logPath + ".1";
                Assert.True(File.Exists(archivePath), "Expected rotated archive file to exist.");

                string archiveText = File.ReadAllText(archivePath);
                Assert.Contains("preexisting-marker", archiveText, StringComparison.Ordinal);

                string activeText = ReadLog(logDir);
                Assert.Contains("Post-rotation marker", activeText, StringComparison.Ordinal);
                Assert.DoesNotContain("preexisting-marker", activeText, StringComparison.Ordinal);

                long activeSize = new FileInfo(logPath).Length;
                Assert.True(activeSize < 1_048_576, "Expected active log file to be below rotation threshold after write.");
            }
            finally
            {
                SafeDeleteDirectory(logDir);
            }
        }
    }

    [Fact]
    public void ConcurrentWrites_PersistAllEntries_AndExceptionDetails()
    {
        lock (LoggerSync)
        {
            string logDir = CreateTempLogDirectory();
            try
            {
                Logger.Shutdown();
                Logger.Initialize(logDir);

                const int entryCount = 40;
                Parallel.For(0, entryCount, i =>
                {
                    Logger.LogInfo("Concurrency", $"entry-{i:D2}");
                });

                var ex = new InvalidOperationException("failure-marker");
                Logger.LogError("Concurrency", "Exception payload check", ex);
                Logger.Shutdown();

                string text = ReadLog(logDir);
                for (int i = 0; i < entryCount; i++)
                {
                    Assert.Contains($"entry-{i:D2}", text, StringComparison.Ordinal);
                }

                Assert.Contains("Exception payload check", text, StringComparison.Ordinal);
                Assert.Contains("exceptionType=System.InvalidOperationException", text, StringComparison.Ordinal);
                Assert.Contains("failure-marker", text, StringComparison.Ordinal);
            }
            finally
            {
                SafeDeleteDirectory(logDir);
            }
        }
    }

    private static string CreateTempLogDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "rv-logger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ReadLog(string logDir)
    {
        string logPath = Path.Combine(logDir, "charthub.log");
        Assert.True(File.Exists(logPath), "Expected logger output file to exist.");
        return File.ReadAllText(logPath);
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp test directories.
        }
    }
}
