using Nornis.Application.Messaging;
using Nornis.Application.Services;

namespace Nornis.Worker;

/// <summary>
/// What the extraction worker does when a delivery fails and the queue will not deliver again.
///
/// Abandoning the last delivery dead-letters the message, so a source that a transient failure
/// put back to Queued would wait for a redelivery that never comes. Failing it here turns that
/// silent wedge into a visible Failed source with the usual Resubmit. The message still goes to
/// the dead-letter queue as before, where the dead-letter alert counts it.
///
/// Kept out of <see cref="ExtractionWorker"/> because the worker's tests drive a hand-maintained
/// copy of its handler; this is called by both, and tested on its own.
/// </summary>
public static class LastDelivery
{
    /// <summary>Whether this delivery is the queue's last: abandoning it dead-letters.</summary>
    public static bool IsLast(long deliveryCount) =>
        deliveryCount >= RedeliveryBackoff.QueueMaxDeliveryCount;

    /// <summary>
    /// On the last delivery of a source extraction, fails the source. Other kinds are left alone:
    /// a relationship backfill reads a Processed source and never moves its status, and a summary
    /// refresh is about an artifact.
    ///
    /// Never throws. The caller abandons the message next, and a failed write here must not stop
    /// that. The source then falls back to the one-hour Retry.
    /// </summary>
    public static async Task GiveUpIfLastAsync(
        IExtractionService extraction,
        ExtractionMessage message,
        long deliveryCount,
        string? failureCategory,
        ILogger logger)
    {
        if (!IsLast(deliveryCount) || message.Kind != ExtractionKind.Extraction)
        {
            return;
        }

        try
        {
            // Not the processor's token: during a deploy it is already cancelled, and this is the
            // last chance anything has to move the source.
            await extraction.GiveUpAsync(message.SourceId, failureCategory, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not mark the source Failed after the last delivery; it stays Queued until the one-hour retry. SourceId={SourceId}",
                message.SourceId);
        }
    }
}
