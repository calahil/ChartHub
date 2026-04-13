using ChartHub.Server.Contracts;

namespace ChartHub.Server.Services;

public interface IVolumeService
{
    bool IsSupportedPlatform { get; }

    int SseHeartbeatSeconds { get; }

    long CurrentChangeStamp { get; }

    Task<VolumeStateResponse> GetStateAsync(CancellationToken cancellationToken);

    Task<VolumeActionResponse> SetMasterVolumeAsync(int valuePercent, CancellationToken cancellationToken);

    Task<VolumeActionResponse> SetSessionVolumeAsync(string sessionId, int valuePercent, CancellationToken cancellationToken);

    Task<bool> WaitForChangeAsync(long observedChangeStamp, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class VolumeServiceException(int statusCode, string errorCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string ErrorCode { get; } = errorCode;
}