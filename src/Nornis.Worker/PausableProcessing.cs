using Nornis.Application.Services;

namespace Nornis.Worker;

/// <summary>
/// Holds a queue processor running until shutdown, and stops it consuming while paid AI is
/// paused.
///
/// **Why stop consuming rather than reschedule.** The obvious reading of "pause" is to keep
/// receiving and put each message back, but a message you have received is a message whose
/// delivery count you have spent — five of those and the queue dead-letters work that was
/// never broken. The O4 spec proposed re-enqueueing a scheduled copy to dodge that;
/// <see cref="RedeliveryBackoff"/> is a written argument against exactly that trick (it
/// resets DeliveryCount, so the dead-letter backstop stops working), and the namespace is
/// Basic tier, where scheduled messages do not exist at all.
///
/// A message nobody receives costs nothing and waits indefinitely. That is what a queue is
/// for, and it is why this is smaller than the mechanism it replaced.
/// </summary>
public static class PausableProcessing
{
    /// <summary>
    /// How often the flag is consulted. The gate caches for about a minute, so the real lag
    /// from flipping the switch to a quiet queue is up to roughly that plus this — call it
    /// ninety seconds. Slower than an in-process flag and faster than a redeploy by two
    /// orders of magnitude, which is the comparison that matters at 2am.
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Runs until <paramref name="stoppingToken"/> cancels, toggling the processor to follow
    /// the pause flag. Returns with the processor stopped.
    /// </summary>
    public static async Task RunUntilStoppedAsync(
        Func<CancellationToken, Task> startProcessing,
        Func<CancellationToken, Task> stopProcessing,
        Func<CancellationToken, Task<AiPauseState>> readPauseState,
        string queueLabel,
        ILogger logger,
        CancellationToken stoppingToken)
    {
        var consuming = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            AiPauseState state;
            try
            {
                state = await readPauseState(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The gate already fails open and logs; this is belt for anything it rethrows.
                // An unreadable flag must never stop the queue — that would turn a database
                // blip into the outage this switch exists to end.
                logger.LogError(ex, "Could not read the AI pause flag for {Queue}; continuing.", queueLabel);
                continue;
            }

            if (state.IsPaused && consuming)
            {
                logger.LogWarning(
                    "AI is paused ({Reason}) — {Queue} stopping. Queued work waits; nothing is dead-lettered.",
                    state.Reason ?? "no reason given", queueLabel);

                // CancellationToken.None: stopping is the point, and passing a token that may
                // already be cancelling would abandon the stop half-done.
                await stopProcessing(CancellationToken.None);
                consuming = false;
            }
            else if (!state.IsPaused && !consuming)
            {
                logger.LogInformation("AI resumed — {Queue} starting.", queueLabel);
                if (!await ProcessorStartup.StartWithRetryAsync(
                        startProcessing, queueLabel, logger, stoppingToken))
                {
                    return;
                }

                consuming = true;
            }
        }

        if (consuming)
        {
            await stopProcessing(CancellationToken.None);
        }
    }
}
