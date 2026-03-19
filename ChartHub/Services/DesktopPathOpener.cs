using System.Diagnostics;

namespace ChartHub.Services;

public interface IDesktopPathOpener
{
    Task OpenDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);
}

public sealed class DesktopPathOpener : IDesktopPathOpener
{
    public Task OpenDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("Directory path is required.", nameof(directoryPath));

        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory does not exist: {directoryPath}");

        var startInfo = BuildStartInfo(directoryPath);
        var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("Failed to start desktop path opener process.");

        return Task.CompletedTask;
    }

    private static ProcessStartInfo BuildStartInfo(string directoryPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directoryPath}\"",
                UseShellExecute = true,
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return new ProcessStartInfo
            {
                FileName = "open",
                ArgumentList = { directoryPath },
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }

        return new ProcessStartInfo
        {
            FileName = "xdg-open",
            ArgumentList = { directoryPath },
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }
}