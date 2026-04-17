using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using ChartHub.Server.Contracts;
using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed partial class DesktopEntryService(
    IOptions<DesktopEntryOptions> options,
    IWebHostEnvironment environment,
    IHudLifecycleService hudLifecycle,
    ILogger<DesktopEntryService> logger) : IDesktopEntryService
{
    private static readonly string[] ExecFieldTokensToStrip =
    [
        "%f",
        "%F",
        "%u",
        "%U",
        "%d",
        "%D",
        "%n",
        "%N",
        "%i",
        "%c",
        "%k",
        "%%",
    ];

    private static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".svg"] = "image/svg+xml",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".ico"] = "image/x-icon",
        [".xpm"] = "image/x-xpixmap",
    };

    private readonly DesktopEntryOptions _options = options.Value;
    private readonly IHudLifecycleService _hudLifecycle = hudLifecycle;
    private readonly ILogger<DesktopEntryService> _logger = logger;
    private readonly string _catalogDirectoryPath = ServerContentPathResolver.Resolve(options.Value.CatalogDirectory, environment.ContentRootPath);
    private readonly string _iconCacheDirectoryPath = ServerContentPathResolver.Resolve(options.Value.IconCacheDirectory, environment.ContentRootPath);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly ConcurrentDictionary<string, CatalogEntry> _catalog = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _trackedProcesses = new(StringComparer.Ordinal);

    public bool IsEnabled => _options.Enabled;

    public bool IsSupportedPlatform => OperatingSystem.IsLinux();

    public int SseIntervalSeconds => Math.Max(1, _options.SseIntervalSeconds);

    public async Task RefreshCatalogAsync(CancellationToken cancellationToken)
    {
        EnsureEnabledAndSupported();

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_iconCacheDirectoryPath);

            if (!Directory.Exists(_catalogDirectoryPath))
            {
                _catalog.Clear();
                LogCatalogDirectoryMissing(_logger, _catalogDirectoryPath);
                return;
            }

            string[] desktopFiles = Directory.GetFiles(_catalogDirectoryPath, "*.desktop", SearchOption.TopDirectoryOnly);
            Dictionary<string, CatalogEntry> discovered = new(StringComparer.Ordinal);

            foreach (string filePath in desktopFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                if (IsExcludedFromCatalog(content))
                {
                    continue;
                }

                string appId = BuildStableEntryId(filePath);
                string name = ReadDesktopField(content, "Name") ?? Path.GetFileNameWithoutExtension(filePath);
                string exec = ReadDesktopField(content, "Exec") ?? string.Empty;
                string iconField = ReadDesktopField(content, "Icon") ?? string.Empty;

                string? cachedIconAbsolutePath = CacheIconFile(iconField, filePath, appId);
                string? iconFileName = cachedIconAbsolutePath is null ? null : Path.GetFileName(cachedIconAbsolutePath);
                string? iconUrl = iconFileName is null
                    ? null
                    : $"/desktopentry-icons/{Uri.EscapeDataString(appId)}/{Uri.EscapeDataString(iconFileName)}";

                discovered[appId] = new CatalogEntry(appId, name, filePath, exec, iconFileName, iconUrl);
            }

            _catalog.Clear();
            foreach ((string key, CatalogEntry value) in discovered)
            {
                _catalog[key] = value;
            }

            TrimTrackedProcessesToCatalog();
            LogCatalogRefreshed(_logger, _catalog.Count, _catalogDirectoryPath);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task<IReadOnlyList<DesktopEntryItemResponse>> ListEntriesAsync(CancellationToken cancellationToken)
    {
        EnsureEnabledAndSupported();

        if (_catalog.IsEmpty)
        {
            await RefreshCatalogAsync(cancellationToken).ConfigureAwait(false);
        }

        var items = _catalog.Values
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry =>
            {
                int? processId = TryGetTrackedRunningProcessId(entry.EntryId, out int pid) ? pid : null;
                return new DesktopEntryItemResponse
                {
                    EntryId = entry.EntryId,
                    Name = entry.Name,
                    Status = processId.HasValue ? "Running" : "Not running",
                    ProcessId = processId,
                    IconUrl = entry.IconUrl,
                };
            })
            .ToList();

        return items;
    }

    public async Task<DesktopEntryActionResponse> ExecuteAsync(string entryId, CancellationToken cancellationToken)
    {
        EnsureEnabledAndSupported();

        if (string.IsNullOrWhiteSpace(entryId))
        {
            throw new DesktopEntryServiceException(StatusCodes.Status400BadRequest, "validation_error", "entryId is required.");
        }

        if (_catalog.IsEmpty)
        {
            await RefreshCatalogAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!_catalog.TryGetValue(entryId, out CatalogEntry? entry))
        {
            throw new DesktopEntryServiceException(StatusCodes.Status404NotFound, "not_found", $"Desktop entry '{entryId}' was not found.");
        }

        if (TryGetTrackedRunningProcessId(entry.EntryId, out int runningPid))
        {
            throw new DesktopEntryServiceException(StatusCodes.Status409Conflict, "already_running", $"Desktop entry is already running with PID {runningPid}.");
        }

        ParsedExec exec = ParseExec(entry.Exec);
        if (string.IsNullOrWhiteSpace(exec.FileName))
        {
            throw new DesktopEntryServiceException(StatusCodes.Status400BadRequest, "invalid_exec", "Desktop entry Exec field is missing or invalid.");
        }

        await _hudLifecycle.SuspendAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exec.FileName,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(entry.DesktopFilePath) ?? "/",
                },
                EnableRaisingEvents = true,
            };

            foreach (string argument in exec.Arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            string capturedEntryId = entry.EntryId;
            process.Exited += (_, _) =>
            {
                _trackedProcesses.TryRemove(capturedEntryId, out _);
                process.Dispose();
                _ = _hudLifecycle.ResumeAsync(CancellationToken.None);
            };

            if (!process.Start())
            {
                process.Dispose();
                throw new DesktopEntryServiceException(StatusCodes.Status500InternalServerError, "execution_failed", "Failed to start process.");
            }

            _trackedProcesses[entry.EntryId] = process.Id;
            LogDesktopEntryExecuted(_logger, entry.EntryId, process.Id);

            return new DesktopEntryActionResponse
            {
                EntryId = entry.EntryId,
                Status = "Running",
                ProcessId = process.Id,
                Message = "Desktop entry started.",
            };
        }
        catch (DesktopEntryServiceException)
        {
            await _hudLifecycle.ResumeAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await _hudLifecycle.ResumeAsync(CancellationToken.None).ConfigureAwait(false);
            LogDesktopEntryExecuteFailed(_logger, entry.EntryId, ex);
            throw new DesktopEntryServiceException(StatusCodes.Status500InternalServerError, "execution_failed", "Failed to execute desktop entry.");
        }
    }

    public async Task<DesktopEntryActionResponse> KillAsync(string entryId, CancellationToken cancellationToken)
    {
        EnsureEnabledAndSupported();

        if (string.IsNullOrWhiteSpace(entryId))
        {
            throw new DesktopEntryServiceException(StatusCodes.Status400BadRequest, "validation_error", "entryId is required.");
        }

        if (!_catalog.ContainsKey(entryId))
        {
            throw new DesktopEntryServiceException(StatusCodes.Status404NotFound, "not_found", $"Desktop entry '{entryId}' was not found.");
        }

        if (!_trackedProcesses.TryGetValue(entryId, out int trackedPid))
        {
            throw new DesktopEntryServiceException(StatusCodes.Status404NotFound, "not_running", "No tracked process exists for this desktop entry.");
        }

        if (!IsProcessRunning(trackedPid))
        {
            _trackedProcesses.TryRemove(entryId, out _);
            return new DesktopEntryActionResponse
            {
                EntryId = entryId,
                Status = "Not running",
                ProcessId = null,
                Message = "Tracked process is no longer running.",
            };
        }

        int killExitCode = await RunProcessAsync("kill", $"-TERM {trackedPid}", cancellationToken).ConfigureAwait(false);
        if (killExitCode != 0)
        {
            throw new DesktopEntryServiceException(StatusCodes.Status500InternalServerError, "kill_failed", "Failed to send SIGTERM to process.");
        }

        await Task.Delay(300, cancellationToken).ConfigureAwait(false);

        if (IsProcessRunning(trackedPid))
        {
            throw new DesktopEntryServiceException(StatusCodes.Status409Conflict, "still_running", "SIGTERM was sent but process is still running.");
        }

        _trackedProcesses.TryRemove(entryId, out _);
        await _hudLifecycle.ResumeAsync(cancellationToken).ConfigureAwait(false);

        return new DesktopEntryActionResponse
        {
            EntryId = entryId,
            Status = "Not running",
            ProcessId = null,
            Message = "Desktop entry process terminated.",
        };
    }

    public bool TryResolveIconFile(string entryId, string fileName, out string iconPath, out string contentType)
    {
        iconPath = string.Empty;
        contentType = "application/octet-stream";

        if (string.IsNullOrWhiteSpace(entryId)
            || string.IsNullOrWhiteSpace(fileName)
            || !_catalog.TryGetValue(entryId, out CatalogEntry? entry)
            || string.IsNullOrWhiteSpace(entry.IconFileName)
            || !string.Equals(entry.IconFileName, fileName, StringComparison.Ordinal))
        {
            return false;
        }

        string fullPath = Path.Combine(_iconCacheDirectoryPath, entry.IconFileName);
        if (!File.Exists(fullPath))
        {
            return false;
        }

        iconPath = fullPath;
        string extension = Path.GetExtension(fullPath);
        contentType = ContentTypes.TryGetValue(extension, out string? known)
            ? known
            : "application/octet-stream";
        return true;
    }

    private void EnsureEnabledAndSupported()
    {
        if (!IsEnabled)
        {
            throw new DesktopEntryServiceException(StatusCodes.Status404NotFound, "feature_disabled", "Desktop entry feature is disabled.");
        }

        if (!IsSupportedPlatform)
        {
            throw new DesktopEntryServiceException(StatusCodes.Status501NotImplemented, "unsupported_platform", "Desktop entry feature is only supported on Linux.");
        }
    }

    private bool TryGetTrackedRunningProcessId(string entryId, out int processId)
    {
        processId = 0;
        if (!_trackedProcesses.TryGetValue(entryId, out int trackedPid))
        {
            return false;
        }

        if (!IsProcessRunning(trackedPid))
        {
            _trackedProcesses.TryRemove(entryId, out _);
            return false;
        }

        processId = trackedPid;
        return true;
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static ParsedExec ParseExec(string execValue)
    {
        if (string.IsNullOrWhiteSpace(execValue))
        {
            return new ParsedExec(string.Empty, []);
        }

        string sanitized = execValue;
        foreach (string token in ExecFieldTokensToStrip)
        {
            sanitized = sanitized.Replace(token, string.Empty, StringComparison.Ordinal);
        }

        sanitized = sanitized.Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return new ParsedExec(string.Empty, []);
        }

        List<string> tokens = TokenizeExecCommand(sanitized);
        if (tokens.Count == 0 || string.IsNullOrWhiteSpace(tokens[0]))
        {
            return new ParsedExec(string.Empty, []);
        }

        return new ParsedExec(tokens[0], tokens.Skip(1).ToArray());
    }

    private static List<string> TokenizeExecCommand(string value)
    {
        List<string> tokens = [];
        StringBuilder current = new();
        bool inSingleQuotes = false;
        bool inDoubleQuotes = false;
        bool escapeNext = false;

        foreach (char ch in value)
        {
            if (escapeNext)
            {
                current.Append(ch);
                escapeNext = false;
                continue;
            }

            if (ch == '\\' && !inSingleQuotes)
            {
                escapeNext = true;
                continue;
            }

            if (ch == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                continue;
            }

            if (ch == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inSingleQuotes && !inDoubleQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (escapeNext)
        {
            current.Append('\\');
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string BuildStableEntryId(string desktopFilePath)
    {
        string normalizedPath = Path.GetFullPath(desktopFilePath);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }

    private static bool IsExcludedFromCatalog(string desktopFileContent)
    {
        return ReadDesktopBooleanField(desktopFileContent, "Hidden")
            || ReadDesktopBooleanField(desktopFileContent, "NoDisplay");
    }

    private static bool ReadDesktopBooleanField(string content, string key)
    {
        string? value = ReadDesktopField(content, key);
        return value is not null
            && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadDesktopField(string content, string key)
    {
        string prefix = $"{key}=";
        foreach (string rawLine in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return null;
    }

    private static string TrimMatchingQuotes(string value)
    {
        if (value.Length >= 2)
        {
            bool quotedWithDoubleQuotes = value[0] == '"' && value[^1] == '"';
            bool quotedWithSingleQuotes = value[0] == '\'' && value[^1] == '\'';
            if (quotedWithDoubleQuotes || quotedWithSingleQuotes)
            {
                return value[1..^1].Trim();
            }
        }

        return value;
    }

    private static async Task<int> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        if (!process.Start())
        {
            return -1;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private string? CacheIconFile(string iconField, string desktopFilePath, string entryId)
    {
        if (string.IsNullOrWhiteSpace(iconField))
        {
            return null;
        }

        string? sourcePath = ResolveIconPath(iconField, desktopFilePath);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        string extension = Path.GetExtension(sourcePath);
        string originalFileName = Path.GetFileNameWithoutExtension(sourcePath);
        string safeOriginalFileName = MakeSafeFileName(originalFileName);
        string cacheFileName = string.IsNullOrWhiteSpace(extension)
            ? $"{entryId}-{safeOriginalFileName}"
            : $"{entryId}-{safeOriginalFileName}{extension}";

        string destinationPath = Path.Combine(_iconCacheDirectoryPath, cacheFileName);
        File.Copy(sourcePath, destinationPath, overwrite: true);
        return destinationPath;
    }

    private static string MakeSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "icon";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(fileName.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "icon" : cleaned;
    }

    private static string? ResolveIconPath(string iconField, string desktopFilePath)
    {
        iconField = TrimMatchingQuotes(iconField.Trim());

        if (Path.IsPathRooted(iconField))
        {
            string rooted = Path.GetFullPath(iconField);
            return File.Exists(rooted) ? rooted : null;
        }

        string desktopDirectory = Path.GetDirectoryName(desktopFilePath) ?? "/";
        string localCandidate = Path.GetFullPath(Path.Combine(desktopDirectory, iconField));
        if (File.Exists(localCandidate))
        {
            return localCandidate;
        }

        string[] candidates =
        [
            Path.Combine("/usr/share/pixmaps", iconField),
            Path.Combine("/usr/share/pixmaps", $"{iconField}.png"),
            Path.Combine("/usr/share/pixmaps", $"{iconField}.svg"),
            Path.Combine("/usr/share/pixmaps", $"{iconField}.xpm"),
        ];

        foreach (string candidate in candidates)
        {
            string fullCandidate = Path.GetFullPath(candidate);
            if (File.Exists(fullCandidate))
            {
                return fullCandidate;
            }
        }

        string[] iconSearchRoots =
        [
            "/usr/share/icons/hicolor",
            "/usr/share/icons",
            "/usr/local/share/icons",
        ];

        foreach (string root in iconSearchRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                string matchName = Path.GetExtension(iconField).Length > 0
                    ? iconField
                    : $"{iconField}.*";

                string? match = Directory
                    .EnumerateFiles(root, matchName, SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(match))
                {
                    return Path.GetFullPath(match);
                }
            }
            catch
            {
                // Ignore inaccessible icon directories and continue best-effort lookup.
            }
        }

        return null;
    }

    private void TrimTrackedProcessesToCatalog()
    {
        foreach (string trackedEntryId in _trackedProcesses.Keys)
        {
            if (!_catalog.ContainsKey(trackedEntryId))
            {
                _trackedProcesses.TryRemove(trackedEntryId, out _);
            }
        }
    }

    [LoggerMessage(
        EventId = 7101,
        Level = LogLevel.Information,
        Message = "DesktopEntry catalog refreshed. Count={Count}, Directory={Directory}")]
    private static partial void LogCatalogRefreshed(ILogger logger, int count, string directory);

    [LoggerMessage(
        EventId = 7102,
        Level = LogLevel.Warning,
        Message = "DesktopEntry catalog directory is missing. Directory={Directory}")]
    private static partial void LogCatalogDirectoryMissing(ILogger logger, string directory);

    [LoggerMessage(
        EventId = 7103,
        Level = LogLevel.Information,
        Message = "DesktopEntry executed. EntryId={EntryId}, ProcessId={ProcessId}")]
    private static partial void LogDesktopEntryExecuted(ILogger logger, string entryId, int processId);

    [LoggerMessage(
        EventId = 7104,
        Level = LogLevel.Warning,
        Message = "DesktopEntry execute failed. EntryId={EntryId}")]
    private static partial void LogDesktopEntryExecuteFailed(ILogger logger, string entryId, Exception exception);

    private sealed record CatalogEntry(
        string EntryId,
        string Name,
        string DesktopFilePath,
        string Exec,
        string? IconFileName,
        string? IconUrl);

    private sealed record ParsedExec(string FileName, IReadOnlyList<string> Arguments);
}

public sealed class DesktopEntryServiceException(int statusCode, string errorCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string ErrorCode { get; } = errorCode;
}
