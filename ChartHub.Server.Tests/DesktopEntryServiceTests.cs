using ChartHub.Server.Contracts;
using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Tests;

public sealed class DesktopEntryServiceTests
{
    [Fact]
    public async Task ExecuteAsyncQuotedExecutablePathStartsProcess()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string root = CreateTempDirectory();
        try
        {
            string iconCacheDirectory = Path.Combine(root, "icon-cache");
            string scriptPath = Path.Combine(root, "duck station.sh");
            await File.WriteAllTextAsync(scriptPath, "#!/bin/sh\nsleep 5\n");
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            string desktopFilePath = Path.Combine(root, "duckstation.desktop");
            await File.WriteAllTextAsync(
                desktopFilePath,
                $"[Desktop Entry]\nName=DuckStation\nExec=\"{scriptPath}\"\n");

            DesktopEntryService sut = new(
                Microsoft.Extensions.Options.Options.Create(new DesktopEntryOptions
                {
                    Enabled = true,
                    CatalogDirectory = root,
                    IconCacheDirectory = iconCacheDirectory,
                    SseIntervalSeconds = 2,
                }),
                new TestHostEnvironment(root),
                NullHudLifecycleService.Instance,
                NullLogger<DesktopEntryService>.Instance);

            await sut.RefreshCatalogAsync(CancellationToken.None);
            ChartHub.Server.Contracts.DesktopEntryItemResponse entry = Assert.Single(await sut.ListEntriesAsync(CancellationToken.None));

            DesktopEntryActionResponse action = await sut.ExecuteAsync(entry.EntryId, CancellationToken.None);

            Assert.Equal("Running", action.Status);
            Assert.True(action.ProcessId > 0);

            DesktopEntryActionResponse killed = await sut.KillAsync(entry.EntryId, CancellationToken.None);
            Assert.Equal("Not running", killed.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshCatalogAsyncQuotedIconPathPopulatesIconUrl()
    {
        string root = CreateTempDirectory();
        try
        {
            string iconCacheDirectory = Path.Combine(root, "icon-cache");
            string iconPath = Path.Combine(root, "duck.png");
            await File.WriteAllBytesAsync(iconPath, [0x89, 0x50, 0x4E, 0x47]);

            string desktopFilePath = Path.Combine(root, "duckstation.desktop");
            await File.WriteAllTextAsync(
                desktopFilePath,
                $"[Desktop Entry]\nName=DuckStation\nExec=/bin/true\nIcon=\"{iconPath}\"\n");

            DesktopEntryService sut = new(
                Microsoft.Extensions.Options.Options.Create(new DesktopEntryOptions
                {
                    Enabled = true,
                    CatalogDirectory = root,
                    IconCacheDirectory = iconCacheDirectory,
                    SseIntervalSeconds = 2,
                }),
                new TestHostEnvironment(root),
                NullHudLifecycleService.Instance,
                NullLogger<DesktopEntryService>.Instance);

            await sut.RefreshCatalogAsync(CancellationToken.None);
            DesktopEntryItemResponse entry = Assert.Single(await sut.ListEntriesAsync(CancellationToken.None));

            Assert.NotNull(entry.IconUrl);
            Assert.StartsWith("/desktopentry-icons/", entry.IconUrl, StringComparison.Ordinal);

            string cachedFileName = entry.IconUrl[(entry.IconUrl.LastIndexOf('/') + 1)..];
            Assert.True(sut.TryResolveIconFile(entry.EntryId, cachedFileName, out string resolvedPath, out string contentType));
            Assert.Equal("image/png", contentType);
            Assert.True(File.Exists(resolvedPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "charthub-desktopentry-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class NullHudLifecycleService : IHudLifecycleService
    {
        public static readonly NullHudLifecycleService Instance = new();

        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ChartHub.Server.Tests";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = contentRootPath;

        public string EnvironmentName { get; set; } = Environments.Development;

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}