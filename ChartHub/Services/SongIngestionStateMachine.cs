namespace ChartHub.Services;

public static class SongIngestionRetryPolicy
{
    public const int MaxDownloadRetries = 2;

    public static bool CanRetryDownloadFailure(int currentRetryCount)
    {
        return currentRetryCount < MaxDownloadRetries;
    }
}

public sealed class SongIngestionStateMachine
{
    private static readonly Dictionary<IngestionState, HashSet<IngestionState>> AllowedTransitions = new()
    {
        [IngestionState.Queued] = [IngestionState.ResolvingSource, IngestionState.Downloaded, IngestionState.Cancelled, IngestionState.Failed],
        [IngestionState.ResolvingSource] = [IngestionState.Queued, IngestionState.Downloading, IngestionState.Failed, IngestionState.Cancelled],
        [IngestionState.Downloading] = [IngestionState.Queued, IngestionState.Downloaded, IngestionState.Failed, IngestionState.Cancelled],
        [IngestionState.Downloaded] = [IngestionState.Queued, IngestionState.Staged, IngestionState.Converting, IngestionState.Installing, IngestionState.Failed],
        [IngestionState.Staged] = [IngestionState.Queued, IngestionState.Converting, IngestionState.Installing, IngestionState.Failed],
        [IngestionState.Converting] = [IngestionState.Queued, IngestionState.Converted, IngestionState.Failed, IngestionState.Cancelled],
        [IngestionState.Converted] = [IngestionState.Queued, IngestionState.Installing, IngestionState.Failed],
        [IngestionState.Installing] = [IngestionState.Queued, IngestionState.Installed, IngestionState.Failed, IngestionState.Cancelled],
        [IngestionState.Installed] = [IngestionState.Queued],
        [IngestionState.Failed] = [IngestionState.Queued, IngestionState.ResolvingSource, IngestionState.Cancelled],
        [IngestionState.Cancelled] = [IngestionState.Queued],
    };

    public bool CanTransition(IngestionState from, IngestionState to)
    {
        return AllowedTransitions.TryGetValue(from, out HashSet<IngestionState>? allowed)
            && allowed.Contains(to);
    }

    public void EnsureCanTransition(IngestionState from, IngestionState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Invalid ingestion transition: {from} -> {to}");
        }
    }

    public IngestionState GetRetryStartState(IngestionState failedState)
    {
        return failedState switch
        {
            IngestionState.Failed => IngestionState.Queued,
            IngestionState.Cancelled => IngestionState.Queued,
            _ => IngestionState.Queued,
        };
    }
}
