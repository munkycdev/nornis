using System.Collections.Concurrent;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// A fixed-size ring of recent AI outcomes per provider. Singleton, so it survives between
/// requests; bounded, so a long-running host cannot grow it (one ring per provider, and there
/// are five).
/// </summary>
public class AiOutcomeMonitor : IAiOutcomeMonitor
{
    /// <summary>
    /// Enough history that one unlucky failure cannot flip the status on its own, small
    /// enough that a genuine outage reaches every slot within a few minutes of traffic.
    /// </summary>
    public const int Capacity = 20;

    private readonly ConcurrentDictionary<AiProvider, Ring> _rings = new();

    public void Record(AiProvider provider, bool succeeded, DateTimeOffset at) =>
        _rings.GetOrAdd(provider, _ => new Ring()).Record(succeeded, at);

    public AiOutcomeSnapshot Snapshot(AiProvider provider, TimeSpan window, DateTimeOffset now) =>
        _rings.TryGetValue(provider, out var ring) ? ring.Snapshot(window, now) : new AiOutcomeSnapshot(0, 0, null);

    private sealed class Ring
    {
        private readonly (DateTimeOffset At, bool Succeeded)[] _entries = new (DateTimeOffset, bool)[Capacity];
        private readonly Lock _gate = new();
        private int _next;
        private int _count;

        public void Record(bool succeeded, DateTimeOffset at)
        {
            lock (_gate)
            {
                _entries[_next] = (at, succeeded);
                _next = (_next + 1) % Capacity;
                if (_count < Capacity)
                {
                    _count++;
                }
            }
        }

        public AiOutcomeSnapshot Snapshot(TimeSpan window, DateTimeOffset now)
        {
            var cutoff = now - window;
            var total = 0;
            var failures = 0;
            DateTimeOffset? lastAt = null;

            lock (_gate)
            {
                for (var i = 0; i < _count; i++)
                {
                    var entry = _entries[i];

                    if (lastAt is null || entry.At > lastAt)
                    {
                        lastAt = entry.At;
                    }

                    if (entry.At < cutoff)
                    {
                        continue;
                    }

                    total++;
                    if (!entry.Succeeded)
                    {
                        failures++;
                    }
                }
            }

            return new AiOutcomeSnapshot(total, failures, lastAt);
        }
    }
}
