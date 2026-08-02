using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Nornis.Worker.Tests;

/// <summary>
/// Starting a queue processor used to be a single call. Host options set
/// BackgroundServiceExceptionBehavior.Ignore so one worker's fault cannot take the other
/// down — which also meant a throw here was swallowed and the worker sat alive and idle.
/// A Service Bus blip at boot halted extraction until somebody happened to deploy again,
/// with nothing anywhere saying so.
/// </summary>
[TestFixture]
public class ProcessorStartupTests
{
    private static readonly TimeSpan NoWait = TimeSpan.FromMilliseconds(1);

    [Test]
    public async Task StartsFirstTime_WithoutRetrying()
    {
        var attempts = 0;

        var started = await ProcessorStartup.StartWithRetryAsync(
            _ => { attempts++; return Task.CompletedTask; },
            "source-extraction", NullLogger.Instance, CancellationToken.None, NoWait);

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.True);
            Assert.That(attempts, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RetriesUntilTheQueueComesBack()
    {
        // The blip this exists for: the namespace refuses the first attempts and then
        // answers. The old code gave up on the first and left the worker idle forever.
        var attempts = 0;

        var started = await ProcessorStartup.StartWithRetryAsync(
            _ =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException(new InvalidOperationException("MessagingEntityNotFound"))
                    : Task.CompletedTask;
            },
            "source-extraction", NullLogger.Instance, CancellationToken.None, NoWait);

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.True);
            Assert.That(attempts, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Shutdown_StopsRetrying_AndReportsNotStarted()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;

        var started = await ProcessorStartup.StartWithRetryAsync(
            _ =>
            {
                attempts++;
                // Shutdown arriving mid-outage must end the loop rather than spin forever.
                if (attempts == 2)
                {
                    cts.Cancel();
                }

                return Task.FromException(new InvalidOperationException("still down"));
            },
            "source-extraction", NullLogger.Instance, cts.Token, NoWait);

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.False, "a cancelled start must not report success");
            Assert.That(attempts, Is.EqualTo(2));
        });
    }

    [Test]
    public void BackoffCeiling_StaysWithinAReasonableRecoveryWindow()
    {
        // A namespace that returns should be picked up in minutes, not hours — the cap is
        // what keeps an exponential from growing past usefulness.
        Assert.That(ProcessorStartup.MaxBackoff, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(5)));
    }
}
