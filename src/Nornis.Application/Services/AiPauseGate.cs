using Microsoft.Extensions.Logging;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>Whether paid AI is currently paused, and the operator's reason if so.</summary>
public record AiPauseState(bool IsPaused, string? Reason)
{
    public static readonly AiPauseState Running = new(false, null);
}

public interface IAiPauseGate
{
    /// <summary>
    /// Cached for roughly a minute. Every paid dispatch calls this, so an uncached read would
    /// add a query per extraction, per Ask, per indexing batch — and the switch does not need
    /// to be instant to be useful. A minute is faster than a redeploy by two orders of
    /// magnitude, which is the comparison that matters.
    /// </summary>
    Task<AiPauseState> GetAsync(CancellationToken ct);
}

/// <summary>
/// Reads the pause flag on a short cache, shared by every host.
///
/// **Fails open, deliberately.** If the flag cannot be read — the database is down, the table
/// is missing on an un-migrated deployment — this reports "not paused". The alternative fails
/// closed and turns a database blip into a total AI outage, which is the incident this switch
/// exists to *end*, not to cause. A pause that needs the database is a pause an operator can
/// see is not working; a phantom pause is one nobody can turn off.
/// </summary>
public class AiPauseGate : IAiPauseGate
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly IOperationalFlagRepository _flags;
    private readonly TimeProvider _time;
    private readonly ILogger<AiPauseGate> _logger;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private AiPauseState _cached = AiPauseState.Running;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public AiPauseGate(
        IOperationalFlagRepository flags,
        ILogger<AiPauseGate> logger,
        TimeProvider? time = null)
    {
        _flags = flags;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<AiPauseState> GetAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        if (now - _cachedAt < CacheDuration)
        {
            return _cached;
        }

        // One refresh at a time. Without this, a burst of extractions on a cold cache each
        // issues its own query — the stampede the cache exists to prevent.
        await _refreshLock.WaitAsync(ct);
        try
        {
            now = _time.GetUtcNow();
            if (now - _cachedAt < CacheDuration)
            {
                return _cached;
            }

            var flag = await _flags.GetAsync(OperationalFlagNames.AiPaused, ct);
            _cached = flag is { Enabled: true } ? new AiPauseState(true, flag.Reason) : AiPauseState.Running;
            _cachedAt = now;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // See the class comment: unreadable means running. Logged every time rather than
            // once, because this is a lever someone may be pulling right now.
            _logger.LogError(ex, "Could not read the AI pause flag; treating AI as running.");
            _cached = AiPauseState.Running;
            _cachedAt = now;
        }
        finally
        {
            _refreshLock.Release();
        }

        return _cached;
    }
}
