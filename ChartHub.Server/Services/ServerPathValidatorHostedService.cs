using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed class ServerPathValidatorHostedService(
    IOptions<ServerPathOptions> options,
    IWebHostEnvironment environment) : IHostedService
{
    private readonly ServerPathOptions _options = options.Value;
    private readonly IWebHostEnvironment _environment = environment;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        IEnumerable<string> requiredPaths =
        [
            ResolvePath(_options.ConfigRoot),
            ResolvePath(_options.ChartHubRoot),
            ResolvePath(_options.DownloadsDir),
            ResolvePath(_options.StagingDir),
            ResolvePath(_options.CloneHeroRoot),
            ResolvePath(_options.CloneHeroPostProcessRoot),
            ResolvePath(_options.CloneHeroArchiveRoot),
        ];

        foreach (string path in requiredPaths)
        {
            Directory.CreateDirectory(path);
            ValidateDirectoryWritable(path);
        }

        string sqlitePath = ResolvePath(_options.SqliteDbPath);
        string? sqliteDir = Path.GetDirectoryName(sqlitePath);
        if (string.IsNullOrWhiteSpace(sqliteDir))
        {
            throw new InvalidOperationException($"Unable to resolve SQLite directory for '{_options.SqliteDbPath}'.");
        }

        Directory.CreateDirectory(sqliteDir);
        ValidateDirectoryWritable(sqliteDir);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private string ResolvePath(string path)
    {
        return ServerContentPathResolver.Resolve(path, _environment.ContentRootPath);
    }

    private static void ValidateDirectoryWritable(string path)
    {
        string probePath = Path.Combine(path, $".probe-{Guid.NewGuid():N}");
        File.WriteAllText(probePath, "ok");
        File.Delete(probePath);
    }
}
