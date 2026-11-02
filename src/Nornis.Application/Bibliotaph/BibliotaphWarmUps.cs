using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;

namespace Nornis.Application.Bibliotaph;

/// <summary>
/// Warm-ups waiting to be sent (feature 31): opening a world asks Bibliotaph to open the GM's
/// library, so the first question doesn't meet "opening". Bounded twice: at most one per world
/// per <see cref="BibliotaphOptions.WarmUpThrottleMinutes"/>, and a queue of 100 that drops when
/// full, because a warm-up is a courtesy and never worth holding a request for.
/// </summary>
public sealed class BibliotaphWarmUps
{
    public const int Capacity = 100;

    private readonly Channel<Guid> _queue = Channel.CreateBounded<Guid>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _last = new();
    private readonly BibliotaphOptions _options;
    private readonly TimeProvider _time;

    public BibliotaphWarmUps(IOptions<BibliotaphOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public ChannelReader<Guid> Reader => _queue.Reader;

    /// <summary>Queues a warm-up for the world unless one went recently. Returns whether it was queued.</summary>
    public bool Request(Guid worldId)
    {
        if (!_options.IsConfigured)
        {
            return false;
        }

        var now = _time.GetUtcNow();
        var every = TimeSpan.FromMinutes(_options.WarmUpThrottleMinutes);
        if (_last.TryGetValue(worldId, out var at) && now - at < every)
        {
            return false;
        }

        _last[worldId] = now;
        return _queue.Writer.TryWrite(worldId);
    }
}
