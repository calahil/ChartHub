using System.IO;
using System.Text;

using ChartHub.Server.Services;

using Microsoft.Extensions.Logging;

namespace ChartHub.Server.Tests;

public sealed class ServerFileLoggerProviderTests
{
    [Fact]
    public void CreateLoggerWritesFormattedLineToWriter()
    {
        StringBuilder sink = new();
        using StringWriter writer = new(sink);
        using ServerFileLoggerProvider sut = new(writer);

        ILogger logger = sut.CreateLogger("TestCategory");
        logger.Log(LogLevel.Information, 0, "hello world", null, (s, _) => s);

        string output = sink.ToString();
        Assert.Contains("TestCategory", output);
        Assert.Contains("hello world", output);
    }

    [Fact]
    public void CreateLoggerIncludesEventIdInOutput()
    {
        StringBuilder sink = new();
        using StringWriter writer = new(sink);
        using ServerFileLoggerProvider sut = new(writer);

        ILogger logger = sut.CreateLogger("Cat");
        logger.Log(LogLevel.Warning, new EventId(42), "msg", null, (s, _) => s);

        Assert.Contains("EventId=42", sink.ToString());
    }

    [Fact]
    public void CreateLoggerIncludesExceptionInOutput()
    {
        StringBuilder sink = new();
        using StringWriter writer = new(sink);
        using ServerFileLoggerProvider sut = new(writer);

        ILogger logger = sut.CreateLogger("Cat");
        InvalidOperationException exception = new("boom");
        logger.Log(LogLevel.Error, 0, "error occurred", exception, (s, _) => s);

        string output = sink.ToString();
        Assert.Contains("error occurred", output);
        Assert.Contains("InvalidOperationException", output);
    }

    [Fact]
    public void CreateLoggerWithNullWriterDoesNotThrow()
    {
        using ServerFileLoggerProvider sut = new(StreamWriter.Null);

        ILogger logger = sut.CreateLogger("Cat");

        Exception? caught = Record.Exception(() =>
            logger.Log(LogLevel.Information, 0, "anything", null, (s, _) => s));
        Assert.Null(caught);
    }

    [Fact]
    public void ConstructorWithValidDirectoryCreatesLogFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            using ServerFileLoggerProvider sut = new(tempDir, "test.log");
            string logPath = Path.Combine(tempDir, "test.log");
            Assert.True(File.Exists(logPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ConstructorWithNullOrWhitespaceDirectoryThrows()
    {
        Assert.Throws<ArgumentException>(() => new ServerFileLoggerProvider("  ", "test.log"));
        Assert.Throws<ArgumentException>(() => new ServerFileLoggerProvider(string.Empty, "test.log"));
    }

    [Fact]
    public void DisposeWithNullWriterDoesNotThrow()
    {
        Exception? caught = Record.Exception(() =>
        {
            ServerFileLoggerProvider sut = new(StreamWriter.Null);
            sut.Dispose();
        });

        Assert.Null(caught);
    }
}
