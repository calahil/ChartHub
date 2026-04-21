using System.Text;

namespace ChartHub.Server.Services;

public enum ServerInstallFileType
{
    Unknown,
    Zip,
    Rar,
    SevenZip,
    Con,
    Sng,
}

public sealed record ServerArtifactClassification(
    ServerInstallFileType FileType,
    string CanonicalExtension)
{
    public bool IsKnown => FileType != ServerInstallFileType.Unknown;
}

public interface IServerInstallFileTypeResolver
{
    Task<ServerArtifactClassification> ClassifyAsync(string artifactPath, CancellationToken cancellationToken = default);

    Task<ServerInstallFileType> ResolveAsync(string artifactPath, CancellationToken cancellationToken = default);
}

public sealed class ServerInstallFileTypeResolver : IServerInstallFileTypeResolver
{
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] RarSignature = Encoding.UTF8.GetBytes("Rar!");
    private static readonly byte[] SevenZipSignature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static readonly byte[] Rb3ConSignature = Encoding.UTF8.GetBytes("CON ");
    private static readonly byte[] SngSignature = Encoding.UTF8.GetBytes("SNGPKG");

    public async Task<ServerArtifactClassification> ClassifyAsync(string artifactPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(artifactPath))
        {
            return Unknown();
        }

        byte[] fileSignature = new byte[Math.Max(Math.Max(ZipSignature.Length, SevenZipSignature.Length), SngSignature.Length)];
        await using FileStream stream = new(artifactPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int read = await stream.ReadAsync(fileSignature.AsMemory(0, fileSignature.Length), cancellationToken).ConfigureAwait(false);
        if (read <= 0)
        {
            return Unknown();
        }

        if (HasSignature(fileSignature, read, SngSignature))
        {
            return new ServerArtifactClassification(ServerInstallFileType.Sng, ".sng");
        }

        if (HasSignature(fileSignature, read, Rb3ConSignature))
        {
            return new ServerArtifactClassification(ServerInstallFileType.Con, ".rb3con");
        }

        if (HasSignature(fileSignature, read, ZipSignature))
        {
            return new ServerArtifactClassification(ServerInstallFileType.Zip, ".zip");
        }

        if (HasSignature(fileSignature, read, RarSignature))
        {
            return new ServerArtifactClassification(ServerInstallFileType.Rar, ".rar");
        }

        if (HasSignature(fileSignature, read, SevenZipSignature))
        {
            return new ServerArtifactClassification(ServerInstallFileType.SevenZip, ".7z");
        }

        return Unknown();
    }

    public async Task<ServerInstallFileType> ResolveAsync(string artifactPath, CancellationToken cancellationToken = default)
    {
        return (await ClassifyAsync(artifactPath, cancellationToken).ConfigureAwait(false)).FileType;
    }

    private static bool HasSignature(byte[] buffer, int read, byte[] expected)
    {
        return read >= expected.Length && buffer.AsSpan(0, expected.Length).SequenceEqual(expected);
    }

    private static ServerArtifactClassification Unknown()
    {
        return new ServerArtifactClassification(ServerInstallFileType.Unknown, string.Empty);
    }
}
