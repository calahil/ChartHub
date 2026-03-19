namespace ChartHub.Services;

public enum InstallStage
{
    Preparing,
    ExtractingArchive,
    Importing,
    ValidatingImport,
    PatchingYaml,
    Building,
    MovingToCloneHero,
    CleaningUp,
    Completed,
    Failed,
    Cancelled,
}

public sealed record InstallProgressUpdate(
    InstallStage Stage,
    string Message,
    double? ProgressPercent = null,
    string? CurrentItemName = null,
    string? LogLine = null,
    bool IsIndeterminate = false);

public sealed record OnyxInstallResult(
    string FinalInstallDirectory,
    string ImportPath,
    string BuildOutputPath);