using Nornis.Application.Ai;

namespace Nornis.Application.Services;

/// <summary>
/// Fixed-size ring of recent AI outcomes. Singleton, so it survives between requests;
/// bounded, so a long-running host cannot grow it.
/// </summary>
public class AiOutcomeMonitor : IAiOutcomeMonitor
{
    /// <summary>
    /// Enough history that one unlucky failure cannot flip the status on its own, small
    /// enough that a genuine outage reaches every slot within a few minutes of traffic.
    /// </summary>
    public const int Capacity = 20;

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
