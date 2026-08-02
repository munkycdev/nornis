namespace Nornis.Worker;

/// <summary>
/// Starting a Service Bus processor, with retries.
///
/// Both queue workers used to call <c>StartProcessingAsync</c> exactly once. Host options
/// set <c>BackgroundServiceExceptionBehavior.Ignore</c> so one worker's fault cannot take
/// the other down — which also meant a throw here was swallowed and the worker sat alive
/// and idle. A Service Bus blip during boot therefore halted extraction until somebody
/// happened to deploy again, with nothing anywhere saying so.
///
/// Retrying forever is deliberate. There is no useful "give up" for a queue processor: if
/// the namespace comes back in ten minutes, the right behaviour is to start consuming.
/// </summary>
public static class ProcessorStartup
{
    /// <summary>Long enough not to hammer a namespace that is genuinely down.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Calls <paramref name="startProcessing"/> until it succeeds or the token cancels,
    /// doubling the wait between attempts up to <see cref="MaxBackoff"/>.
    /// </summary>
    /// <returns>True when processing started; false when shutdown cancelled the attempt.</returns>
    public static async Task<bool> StartWithRetryAsync(
        Func<CancellationToken, Task> startProcessing,
        string queueLabel,
        ILogger logger,
        CancellationToken stoppingToken,
        TimeSpan? initialBackoff = null)
    {
        var backoff = initialBackoff ?? TimeSpan.FromSeconds(5);
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            attempt++;
            try
            {
                await startProcessing(stoppingToken);
                if (attempt > 1)
                {
                    logger.LogInformation(
                        "Queue processor started for {Queue} after {Attempts} attempts.", queueLabel, attempt);
                }

                return true;
            }
            catch (Exception ex)
            {
                // Shutdown racing a failed start is a shutdown, not an incident — leave
                // quietly rather than letting the exception escape into the host.
                if (stoppingToken.IsCancellationRequested)
                {
                    return false;
                }

                // Logged at Error every time, not just the first: a processor that never
                // starts is an outage, and one line at boot is easy to miss forever.
                logger.LogError(ex,
                    "Failed to start queue processor for {Queue} (attempt {Attempt}). Retrying in {Backoff}.",
                    queueLabel, attempt, backoff);
            }

            try
            {
                await Task.Delay(backoff, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }

        return false;
    }
}
