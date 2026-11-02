using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Bibliotaph;

public interface IBibliotaphOutboxDelivery
{
    /// <summary>Delivers what is due, settles what is too old, and purges what has settled and old reading records.</summary>
    Task RunOnceAsync(CancellationToken ct);
}

/// <summary>
/// One pass of the Bibliotaph outbox (feature 31, Requirement 2): each user's due events, oldest
/// first, in batches no bigger than the webhook takes, through that user's own connection.
///
/// <para>
/// No claim or lease: Bibliotaph applies an event once by its id, so a pass that overlaps
/// another (two API replicas, a rolling deploy) costs a duplicate delivery, never a wrong
/// collection. A batch is the unit of success: Bibliotaph answers for the whole batch.
/// </para>
///
/// <para>
/// What a failure does: no connection any more means the events have no one to deliver them,
/// and they are failed now. Anything else, a refused grant included, waits with backoff, so
/// a GM who reconnects within the week loses nothing. Past <see cref="BibliotaphOptions.OutboxMaxAgeDays"/>
/// an event is failed and logged; reconnecting or Resync sends a fresh <c>created</c> for every
/// world, which repairs what it would have said.
/// </para>
/// </summary>
public sealed class BibliotaphOutboxDelivery : IBibliotaphOutboxDelivery
{
    /// <summary>How many due rows one pass reads: a few batches' worth, so one busy user can't starve the rest.</summary>
    public const int RowsPerPass = 500;

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    /// <summary>Reading records purged per pass, so a backlog drains without one long delete.</summary>
    public const int ReadingRecordsPurgedPerPass = 1000;

    private readonly IBibliotaphOutboxRepository _outbox;
    private readonly ILibraryReadingRecordRepository _readingRecords;
    private readonly IBibliotaphClient _client;
    private readonly BibliotaphOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BibliotaphOutboxDelivery> _logger;

    public BibliotaphOutboxDelivery(
        IBibliotaphOutboxRepository outbox,
        ILibraryReadingRecordRepository readingRecords,
        IBibliotaphClient client,
        IOptions<BibliotaphOptions> options,
        TimeProvider time,
        ILogger<BibliotaphOutboxDelivery> logger)
    {
        _outbox = outbox;
        _readingRecords = readingRecords;
        _client = client;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    /// <summary>The wait after the <paramref name="attempts"/>th failed attempt: 1, 2, 4… minutes, at most an hour.</summary>
    public static TimeSpan Backoff(int attempts)
    {
        var exponent = Math.Clamp(attempts - 1, 0, 10);
        var wait = TimeSpan.FromMinutes(1 << exponent);
        return wait < MaxBackoff ? wait : MaxBackoff;
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var due = await _outbox.ListDueAsync(now, RowsPerPass, ct);

        foreach (var user in due.GroupBy(e => e.UserId))
        {
            foreach (var batch in user.OrderBy(e => e.OccurredAt).Chunk(_options.OutboxBatchSize))
            {
                await DeliverAsync(user.Key, batch, now, ct);
            }
        }

        var purged = await _outbox.PurgeSettledAsync(now.AddDays(-_options.OutboxRetentionDays), ct);
        if (purged > 0)
        {
            _logger.LogInformation("Bibliotaph outbox purged {Count} settled events", purged);
        }

        // Who opened which book is kept 90 days (feature 31, Requirement 4); the same tick ages it out.
        await _readingRecords.PurgeOlderThanAsync(
            now.AddDays(-_options.ReadingRecordRetentionDays), ReadingRecordsPurgedPerPass, ct);
    }

    private async Task DeliverAsync(Guid userId, BibliotaphWorldEvent[] batch, DateTimeOffset now, CancellationToken ct)
    {
        var messages = batch
            .Select(e => new BibliotaphWorldEventMessage(e.Id, BibliotaphWireNames.Of(e.Kind), e.WorldId, e.WorldName, e.OccurredAt))
            .ToList();
        var ids = batch.Select(e => e.Id).ToList();

        var result = await _client.SendWorldEventsAsync(userId, messages, ct);
        if (result.IsSuccess)
        {
            await _outbox.MarkDeliveredAsync(ids, now, ct);
            return;
        }

        var failure = result.Failure!.Value;
        if (failure == BibliotaphFailure.NoConnection)
        {
            await _outbox.MarkFailedAsync(ids, now, ct);
            _logger.LogWarning(
                "Bibliotaph outbox: user {UserId} has no connection; {Count} events failed", userId, ids.Count);
            return;
        }

        var tooOld = batch.Where(e => now - e.OccurredAt >= TimeSpan.FromDays(_options.OutboxMaxAgeDays)).ToList();
        foreach (var expired in tooOld)
        {
            _logger.LogError(
                "Bibliotaph outbox gave up on {Kind} for world {WorldId} after {Attempts} attempts ({Failure})",
                BibliotaphWireNames.Of(expired.Kind), expired.WorldId, expired.Attempts + 1, failure);
        }

        if (tooOld.Count > 0)
        {
            await _outbox.MarkFailedAsync(tooOld.Select(e => e.Id).ToList(), now, ct);
        }

        // Rows in one batch can have different attempt counts (a backfill joining older rows),
        // so each count gets its own wait.
        foreach (var group in batch.Except(tooOld).GroupBy(e => e.Attempts))
        {
            await _outbox.MarkAttemptFailedAsync(
                group.Select(e => e.Id).ToList(), now + Backoff(group.Key + 1), failure.ToString(), ct);
        }
    }
}
