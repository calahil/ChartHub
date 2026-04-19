using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ChartHub.Server.Services;

public sealed class InputConnectionTracker : IInputConnectionTracker, IDisposable
{
    private readonly object _lock = new();
    private readonly List<Channel<HudStatusUpdate>> _subscribers = [];
    private int _activeConnectionCount;
    private string? _connectedDeviceName;

    public int ActiveConnectionCount => Volatile.Read(ref _activeConnectionCount);

    public bool RegisterConnection(string deviceName)
    {
        lock (_lock)
        {
            // Allow the same device to open additional endpoints (controller/touchpad/keyboard).
            // Reject a different device while the slot is occupied.
            if (_activeConnectionCount > 0
                && !string.Equals(_connectedDeviceName, deviceName, StringComparison.Ordinal))
            {
                return false;
            }

            _activeConnectionCount++;
            _connectedDeviceName = deviceName;
        }

        Broadcast();
        return true;
    }

    public void UnregisterConnection(string deviceName)
    {
        lock (_lock)
        {
            if (!string.Equals(_connectedDeviceName, deviceName, StringComparison.Ordinal))
            {
                return;
            }

            int next = Math.Max(0, _activeConnectionCount - 1);
            _activeConnectionCount = next;

            if (_activeConnectionCount == 0)
            {
                _connectedDeviceName = null;
            }
        }

        Broadcast();
    }

    public async IAsyncEnumerable<HudStatusUpdate> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<HudStatusUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

        lock (_lock)
        {
            _subscribers.Add(channel);
        }

        // Immediately emit the current state so the subscriber has an initial value.
        yield return CurrentStatus();

        try
        {
            await foreach (HudStatusUpdate update in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
        }
        finally
        {
            lock (_lock)
            {
                _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (Channel<HudStatusUpdate> channel in _subscribers)
            {
                channel.Writer.TryComplete();
            }

            _subscribers.Clear();
        }
    }

    private HudStatusUpdate CurrentStatus()
    {
        lock (_lock)
        {
            return new HudStatusUpdate(_activeConnectionCount, _connectedDeviceName);
        }
    }

    private void Broadcast()
    {
        HudStatusUpdate update = CurrentStatus();

        lock (_lock)
        {
            foreach (Channel<HudStatusUpdate> channel in _subscribers)
            {
                channel.Writer.TryWrite(update);
            }
        }
    }
}
