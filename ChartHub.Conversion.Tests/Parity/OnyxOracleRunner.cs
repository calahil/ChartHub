using System.Diagnostics;

namespace ChartHub.Conversion.Tests.Parity;

internal static class OnyxOracleRunner
{
    internal static async Task EnsureOracleOutputAsync(string inputPath, string outputPath)
    {
        if (Directory.Exists(outputPath) && !ParityPaths.ShouldForceRegenerateOutputs())
        {
            return;
        }

        if (Directory.Exists(outputPath))
        {
            Directory.Delete(outputPath, recursive: true);
        }

        Directory.CreateDirectory(outputPath);

        string? binaryPath = Environment.GetEnvironmentVariable(ParityPaths.OracleBinaryEnv);
        if (string.IsNullOrWhiteSpace(binaryPath))
        {
            throw new InvalidOperationException($"{ParityPaths.OracleBinaryEnv} must be set when oracle parity is enabled.");
        }

        string? argsTemplate = Environment.GetEnvironmentVariable(ParityPaths.OracleArgsTemplateEnv);
        if (string.IsNullOrWhiteSpace(argsTemplate))
        {
            throw new InvalidOperationException($"{ParityPaths.OracleArgsTemplateEnv} must be set. Use placeholders {{input}} and {{output}}.");
        }

        string args = argsTemplate
            .Replace("{input}", inputPath, StringComparison.Ordinal)
            .Replace("{output}", outputPath, StringComparison.Ordinal);

        ProcessStartInfo startInfo = new()
        {
            FileName = binaryPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (string argument in SplitArguments(args))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Onyx oracle process.");

        int timeoutMs = checked(ParityPaths.GetOracleTimeoutSeconds() * 1000);
        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();

        Task waitForExitTask = process.WaitForExitAsync();
        Task completed = await Task.WhenAny(waitForExitTask, Task.Delay(timeoutMs)).ConfigureAwait(false);
        if (completed != waitForExitTask)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // best effort kill
            }

            throw new TimeoutException($"Onyx oracle process exceeded timeout of {ParityPaths.GetOracleTimeoutSeconds()} seconds.");
        }

        string stdout = await stdOutTask.ConfigureAwait(false);
        string stderr = await stdErrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Onyx oracle failed with code {process.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }
    }

    private static IReadOnlyList<string> SplitArguments(string args)
    {
        List<string> result = [];
        if (string.IsNullOrWhiteSpace(args))
        {
            return result;
        }

        var current = new System.Text.StringBuilder(args.Length);
        bool inQuotes = false;

        for (int i = 0; i < args.Length; i++)
        {
            char ch = args[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
