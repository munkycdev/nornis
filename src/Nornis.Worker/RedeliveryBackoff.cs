using Azure.Messaging.ServiceBus;

namespace Nornis.Worker;

/// <summary>
/// Waits before releasing a failed message back to the queue.
///
/// Abandoning makes a message available again immediately, so the previous behaviour answered a
/// throttle with an instant re-request — the textbook way to extend a throttle window. Each
/// redelivery re-runs the whole pipeline: context assembly, blob reads, a fresh model call. With
/// the production queues at <c>MaxDeliveryCount = 5</c>, one throttled note could burn five full
/// attempts in the time the service needed to recover from the first.
///
/// <para><b>Why a delay rather than a scheduled re-enqueue.</b> Re-enqueuing a scheduled copy is
/// the Service Bus-native backoff and would free the consumer while waiting — but it resets
/// <c>DeliveryCount</c>, so the queue's dead-letter backstop stops working and has to be replaced
/// by an attempt counter carried in the message. Getting that wrong turns a bounded retry into an
/// unbounded one, which is worse than the problem. The namespace is also Basic tier, where
/// scheduling support is not something to assume. Holding the lock costs a worker slot for the
/// duration; on a queue that exists to absorb bursts, that backpressure is the point.</para>
///
/// <para><b>On lock expiry.</b> Auto-renewal keeps running during the wait, but
/// <c>MaxAutoLockRenewalDuration</c> is a ceiling measured from receipt across the whole handler —
/// so what matters is processing time plus this delay, not the delay alone. A handwriting source
/// can spend four minutes in the handler before the backoff is even reached, which on a late
/// delivery can cross the 5-minute extraction ceiling. That is tolerated rather than prevented:
/// abandoning a lost lock is harmless, because Service Bus redelivers the message anyway.</para>
/// </summary>
public static class RedeliveryBackoff
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Mirrors <c>MaxDeliveryCount</c> on both production queues (see provision-azure.ps1 and
    /// servicebus-emulator.json). Abandoning on the final delivery dead-letters the message
    /// instead of redelivering it, so waiting first buys nothing — it just holds the worker's one
    /// slot while a message with no retries left goes to the dead-letter queue.
    /// </summary>
    public const int QueueMaxDeliveryCount = 5;

    /// <summary>
    /// How long to wait before the given delivery attempt is released. Doubles per attempt:
    /// 5s, 10s, 20s, 40s across the four deliveries that retry — the fifth dead-letters
    /// instead of waiting. That is 75 seconds of total backoff before the message dead-letters.
    /// </summary>
    public static TimeSpan DelayFor(long deliveryCount)
    {
        // The last delivery is about to dead-letter, not retry. During a sustained outage with a
        // full queue this would otherwise add a minute of idle worker time per message, on top of
        // an outage, for no benefit.
        if (deliveryCount >= QueueMaxDeliveryCount)
        {
            return TimeSpan.Zero;
        }

        // DeliveryCount is 1 on the first delivery. Guard anyway: a 0 or negative would shift the
        // exponent negative and produce a nonsense delay.
        var attempt = Math.Max(1, deliveryCount);

        // The early return above bounds attempt to 1..4, so the exponent is at most 3.
        return TimeSpan.FromSeconds(BaseDelay.TotalSeconds * Math.Pow(2, attempt - 1));
    }

    /// <summary>
    /// The wait itself, separated from Service Bus so it can be tested. This is the half with the
    /// behaviour worth pinning: which delay is chosen, and that a cancelled token cuts the wait
    /// short instead of holding a deploy open for it.
    /// </summary>
    public static async Task WaitAsync(
        long deliveryCount,
        CancellationToken cancellationToken,
        Action<TimeSpan, long>? onDelaying = null)
    {
        var delay = DelayFor(deliveryCount);
        onDelaying?.Invoke(delay, deliveryCount);

        if (delay == TimeSpan.Zero)
        {
            return;
        }

        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down. Skip the remaining wait so the message is released promptly and the
            // next replica can pick it up, rather than sitting locked until the lock expires.
        }
    }

    /// <summary>
    /// Waits, then abandons — so the message becomes available again only after the backoff.
    ///
    /// The abandon deliberately uses <see cref="CancellationToken.None"/>. During a deploy the
    /// processor's token is already cancelled, and abandoning with it throws, leaving the message
    /// locked until it expires. Releasing it promptly is the whole point of abandoning.
    /// </summary>
    public static async Task DelayThenAbandonAsync(
        ProcessMessageEventArgs args,
        Action<TimeSpan, long>? onDelaying = null)
    {
        await WaitAsync(args.Message.DeliveryCount, args.CancellationToken, onDelaying);

        try
        {
            await args.AbandonMessageAsync(args.Message, cancellationToken: CancellationToken.None);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessageLockLost)
        {
            // The lock expired while we waited — possible when a long handler plus this delay
            // together exceed MaxAutoLockRenewalDuration. Service Bus will redeliver the message
            // regardless, which is what abandoning was for, so there is nothing to recover.
            //
            // Swallowed rather than rethrown because the callers invoke this from inside their
            // try: an escaping exception lands in their catch, which calls this again and waits
            // out a second full backoff before failing the same way.
        }
    }
}
