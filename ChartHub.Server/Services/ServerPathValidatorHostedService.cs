using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed partial class ServerPathValidatorHostedService(
    IOptions<ServerPathOptions> options,
    IWebHostEnvironment environment,
    ILogger<ServerPathValidatorHostedService> logger) : IHostedService
{
    private readonly ServerPathOptions _options = options.Value;
    private readonly IWebHostEnvironment _environment = environment;
    private readonly ILogger<ServerPathValidatorHostedService> _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        IEnumerable<string> requiredPaths =
        [
            ResolvePath(_options.ConfigRoot),
            ResolvePath(_options.ChartHubRoot),
            ResolvePath(_options.DownloadsDir),
            ResolvePath(_options.StagingDir),
            ResolvePath(_options.CloneHeroRoot),
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

        try
        {
            _ = ServerOnyxInstallService.ResolveOnyxExecutablePath();
        }
        catch (FileNotFoundException)
        {
            LogOnyxExecutableNotFound(_logger);
        }

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

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Onyx executable not found in trusted server locations. Expected under tools/onyx rooted at current directory or app base directory.")]
    private static partial void LogOnyxExecutableNotFound(ILogger logger);
}
