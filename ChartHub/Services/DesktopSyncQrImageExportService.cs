namespace ChartHub.Services;

public interface IDesktopSyncQrImageExportService
{
    string? ExportDesktopQrImage(byte[] pngBytes);
}

public sealed class DesktopSyncQrImageExportService : IDesktopSyncQrImageExportService
{
    private const string ExportFileName = "desktop-sync-qr.png";

    public string? ExportDesktopQrImage(byte[] pngBytes)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);

        string? exportPath = ResolveExportPath();
        if (string.IsNullOrWhiteSpace(exportPath))
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
        File.WriteAllBytes(exportPath, pngBytes);
        return exportPath;
    }

    private static string? ResolveExportPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            string solutionPath = Path.Combine(current.FullName, "ChartHub.sln");
            string resourcesPath = Path.Combine(current.FullName, "ChartHub", "Resources", "Raw");
            if (File.Exists(solutionPath) && Directory.Exists(resourcesPath))
            {
                return Path.Combine(resourcesPath, ExportFileName);
            }

            current = current.Parent;
        }

        return null;
    }
}
