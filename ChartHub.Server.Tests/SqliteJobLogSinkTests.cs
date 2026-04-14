using System;
using System.Collections.Generic;
using System.IO;

using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Xunit;

namespace ChartHub.Server.Tests;

public sealed class SqliteJobLogSinkTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteJobLogSink _sut;

    public SqliteJobLogSinkTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"charthub-test-{Guid.NewGuid():N}.db");

        IOptions<ServerPathOptions> options = Microsoft.Extensions.Options.Options.Create(new ServerPathOptions { SqliteDbPath = _dbPath });
        var env = new TestHostEnvironment(Path.GetTempPath());
        _sut = new SqliteJobLogSink(options, env);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public void AddThenGetLogsReturnsEntryForJob()
    {
        var jobId = Guid.NewGuid();

        _sut.Add(jobId, LogLevel.Information, new EventId(2101, "InstallStarted"), "TestCategory", "Install started.", null);

        IReadOnlyList<JobLogEntry> logs = _sut.GetLogs(jobId);
        Assert.Single(logs);
        JobLogEntry entry = logs[0];
        Assert.Equal("Information", entry.Level);
        Assert.Equal(2101, entry.EventId);
        Assert.Equal("TestCategory", entry.Category);
        Assert.Equal("Install started.", entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public void AddMultipleEntriesReturnedInInsertionOrder()
    {
        var jobId = Guid.NewGuid();

        _sut.Add(jobId, LogLevel.Information, new EventId(2101), null, "First", null);
        _sut.Add(jobId, LogLevel.Warning, new EventId(2102), null, "Second", null);
        _sut.Add(jobId, LogLevel.Error, new EventId(2103), null, "Third", "ex");

        IReadOnlyList<JobLogEntry> logs = _sut.GetLogs(jobId);
        Assert.Equal(3, logs.Count);
        Assert.Equal("First", logs[0].Message);
        Assert.Equal("Second", logs[1].Message);
        Assert.Equal("Third", logs[2].Message);
        Assert.Equal("ex", logs[2].Exception);
    }

    [Fact]
    public void GetLogsDifferentJobIdReturnsEmpty()
    {
        var jobIdA = Guid.NewGuid();
        var jobIdB = Guid.NewGuid();

        _sut.Add(jobIdA, LogLevel.Information, new EventId(2101), null, "Only for A", null);

        IReadOnlyList<JobLogEntry> logs = _sut.GetLogs(jobIdB);
        Assert.Empty(logs);
    }

    [Fact]
    public void GetLogsUnknownJobReturnsEmpty()
    {
        IReadOnlyList<JobLogEntry> logs = _sut.GetLogs(Guid.NewGuid());
        Assert.Empty(logs);
    }

    [Fact]
    public void AddWithExceptionStoresExceptionText()
    {
        var jobId = Guid.NewGuid();
        _sut.Add(jobId, LogLevel.Error, new EventId(2110), "Cat", "Boom", "System.InvalidOperationException: boom");

        IReadOnlyList<JobLogEntry> logs = _sut.GetLogs(jobId);
        Assert.Equal("System.InvalidOperationException: boom", logs[0].Exception);
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = contentRootPath;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Test";
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = contentRootPath;
    }
}
