using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ChartHub.Server.Services;

public sealed class InputConnectionTracker : IInputConnectionTracker, IDisposable
{
    private readonly object _lock = new();
    private readonly List<Channel<int>> _subscribers = [];
    private int _activeConnectionCount;

    public int ActiveConnectionCount => Volatile.Read(ref _activeConnectionCount);

    public void RegisterConnection()
    {
        int next = Interlocked.Increment(ref _activeConnectionCount);
        Broadcast(next);
    }

    public void UnregisterConnection()
    {
        int next = Math.Max(0, Interlocked.Decrement(ref _activeConnectionCount));
        Broadcast(next);
    }

    public async IAsyncEnumerable<int> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        {
            SingleReader = true,
        });

        lock (_lock)
        {
            _subscribers.Add(channel);
        }

        // Immediately emit the current count so the subscriber has an initial value.
        yield return ActiveConnectionCount;

        try
        {
            await foreach (int count in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return count;
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
            foreach (Channel<int> channel in _subscribers)
            {
                channel.Writer.TryComplete();
            }

            _subscribers.Clear();
        }
    }

    private void Broadcast(int count)
    {
        lock (_lock)
        {
            foreach (Channel<int> channel in _subscribers)
            {
                channel.Writer.TryWrite(count);
            }
        }
    }
}
