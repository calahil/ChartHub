using System.Text;

namespace ChartHub.Server.Services;

public enum ServerInstallFileType
{
    Unknown,
    Zip,
    Rar,
    SevenZip,
    Con,
}

public interface IServerInstallFileTypeResolver
{
    Task<ServerInstallFileType> ResolveAsync(string artifactPath, CancellationToken cancellationToken = default);
}

public sealed class ServerInstallFileTypeResolver : IServerInstallFileTypeResolver
{
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] RarSignature = Encoding.UTF8.GetBytes("Rar!");
    private static readonly byte[] Rb3ConSignature = Encoding.UTF8.GetBytes("CON");
    private static readonly byte[] SevenZipSignature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

    public async Task<ServerInstallFileType> ResolveAsync(string artifactPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(artifactPath))
        {
            return ServerInstallFileType.Unknown;
        }

        string extension = Path.GetExtension(artifactPath).ToLowerInvariant();
        if (extension is ".zip")
        {
            return ServerInstallFileType.Zip;
        }

        if (extension is ".rar")
        {
            return ServerInstallFileType.Rar;
        }

        if (extension is ".7z")
        {
            return ServerInstallFileType.SevenZip;
        }

        if (extension is ".con" or ".rb3con")
        {
            return ServerInstallFileType.Con;
        }

        byte[] fileSignature = new byte[6];
        await using FileStream stream = new(artifactPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int read = await stream.ReadAsync(fileSignature.AsMemory(0, fileSignature.Length), cancellationToken).ConfigureAwait(false);
        if (read <= 0)
        {
            return ServerInstallFileType.Unknown;
        }

        if (read >= ZipSignature.Length && fileSignature.AsSpan(0, ZipSignature.Length).SequenceEqual(ZipSignature))
        {
            return ServerInstallFileType.Zip;
        }

        if (read >= RarSignature.Length && fileSignature.AsSpan(0, RarSignature.Length).SequenceEqual(RarSignature))
        {
            return ServerInstallFileType.Rar;
        }

        if (read >= SevenZipSignature.Length && fileSignature.AsSpan(0, SevenZipSignature.Length).SequenceEqual(SevenZipSignature))
        {
            return ServerInstallFileType.SevenZip;
        }

        if (read >= Rb3ConSignature.Length && fileSignature.AsSpan(0, Rb3ConSignature.Length).SequenceEqual(Rb3ConSignature))
        {
            return ServerInstallFileType.Con;
        }

        return ServerInstallFileType.Unknown;
    }
}
