using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ChartHub.Server.Services;

public sealed class PresenceTracker : IPresenceTracker, IDisposable
{
    private readonly object _lock = new();
    private readonly List<Channel<PresenceUpdate>> _subscribers = [];
    private string? _deviceName;
    private string? _userEmail;

    public bool IsAnyonePresent
    {
        get
        {
            lock (_lock)
            {
                return _deviceName is not null;
            }
        }
    }

    public string? ConnectedDeviceName
    {
        get
        {
            lock (_lock)
            {
                return _deviceName;
            }
        }
    }

    public string? ConnectedUserEmail
    {
        get
        {
            lock (_lock)
            {
                return _userEmail;
            }
        }
    }

    public bool Register(string deviceName, string userEmail)
    {
        lock (_lock)
        {
            // Allow the same device/user to re-register (e.g. reconnect after network blip).
            if (_deviceName is not null
                && !string.Equals(_deviceName, deviceName, StringComparison.Ordinal))
            {
                return false;
            }

            _deviceName = deviceName;
            _userEmail = userEmail;
        }

        Broadcast();
        return true;
    }

    public void Unregister(string deviceName)
    {
        lock (_lock)
        {
            if (!string.Equals(_deviceName, deviceName, StringComparison.Ordinal))
            {
                return;
            }

            _deviceName = null;
            _userEmail = null;
        }

        Broadcast();
    }

    public async IAsyncEnumerable<PresenceUpdate> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<PresenceUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

        lock (_lock)
        {
            _subscribers.Add(channel);
        }

        // Emit current state immediately so new subscribers don't wait for a change.
        yield return CurrentUpdate();

        try
        {
            await foreach (PresenceUpdate update in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
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
            foreach (Channel<PresenceUpdate> channel in _subscribers)
            {
                channel.Writer.TryComplete();
            }

            _subscribers.Clear();
        }
    }

    private PresenceUpdate CurrentUpdate()
    {
        lock (_lock)
        {
            return new PresenceUpdate(_deviceName is not null, _deviceName, _userEmail);
        }
    }

    private void Broadcast()
    {
        PresenceUpdate update = CurrentUpdate();

        lock (_lock)
        {
            foreach (Channel<PresenceUpdate> channel in _subscribers)
            {
                channel.Writer.TryWrite(update);
            }
        }
    }
}
