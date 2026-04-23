namespace ChartHub.Conversion.Tests.Parity;

internal static class ParityPaths
{
    internal const string OracleEnableEnv = "CH_PARITY_ENABLE_ORACLE";
    internal const string OracleBinaryEnv = "CH_PARITY_ONYX_BIN";
    internal const string OracleArgsTemplateEnv = "CH_PARITY_ONYX_ARGS_TEMPLATE";
    internal const string OracleTimeoutSecondsEnv = "CH_PARITY_ONYX_TIMEOUT_SECONDS";
    internal const string OracleArtifactsRootEnv = "CH_PARITY_ARTIFACTS_ROOT";
    internal const string OracleForceRegenerateEnv = "CH_PARITY_FORCE_REGEN";
    internal const string OracleUpdateChecksumsEnv = "CH_PARITY_UPDATE_CHECKSUMS";

    internal static bool IsOracleEnabled()
    {
        return string.Equals(Environment.GetEnvironmentVariable(OracleEnableEnv), "1", StringComparison.Ordinal);
    }

    internal static bool IsChecksumUpdateEnabled()
    {
        return string.Equals(Environment.GetEnvironmentVariable(OracleUpdateChecksumsEnv), "1", StringComparison.Ordinal);
    }

    internal static bool ShouldForceRegenerateOutputs()
    {
        return string.Equals(Environment.GetEnvironmentVariable(OracleForceRegenerateEnv), "1", StringComparison.Ordinal);
    }

    internal static int GetOracleTimeoutSeconds()
    {
        string? raw = Environment.GetEnvironmentVariable(OracleTimeoutSecondsEnv);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 300;
        }

        if (!int.TryParse(raw, out int timeout) || timeout <= 0)
        {
            throw new InvalidOperationException($"{OracleTimeoutSecondsEnv} must be a positive integer when set.");
        }

        return timeout;
    }

    internal static string GetRepositoryRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine(current, "ChartHub.sln");
            if (File.Exists(candidate))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Unable to locate repository root (ChartHub.sln).");
    }

    internal static string GetFixtureManifestPath(string repoRoot)
    {
        return Path.Combine(repoRoot, "parity", "fixtures.yaml");
    }

    internal static string GetChecksumManifestPath(string repoRoot)
    {
        return Path.Combine(repoRoot, "parity", "checksums", "manifest.yaml");
    }

    internal static string GetArtifactsRoot(string repoRoot)
    {
        return Environment.GetEnvironmentVariable(OracleArtifactsRootEnv)
            ?? Path.Combine(repoRoot, ".parity-artifacts");
    }

    internal static string GetOracleFixtureOutputPath(string artifactsRoot, string fixtureId)
    {
        return Path.Combine(artifactsRoot, "onyx", fixtureId);
    }

    internal static string GetChartHubFixtureOutputPath(string artifactsRoot, string fixtureId)
    {
        return Path.Combine(artifactsRoot, "charthub", fixtureId);
    }
}
