namespace ChartHub.Services;

public interface ILocalFileDeletionService
{
    Task DeletePathIfExistsAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class LocalFileDeletionService : ILocalFileDeletionService
{
    public Task DeletePathIfExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.CompletedTask;
        }

        if (File.Exists(path))
        {
            File.Delete(path);
            return Task.CompletedTask;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        return Task.CompletedTask;
    }
}
