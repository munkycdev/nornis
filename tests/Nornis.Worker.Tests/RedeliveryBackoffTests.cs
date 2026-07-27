using Nornis.Worker;
using NUnit.Framework;

namespace Nornis.Worker.Tests;

/// <summary>
/// Backoff before a failed message is released back to the queue.
///
/// Abandoning makes a message available immediately, so the previous behaviour answered an Azure
/// OpenAI throttle with an instant re-request — and each redelivery re-runs context assembly,
/// blob reads and a fresh model call. The production queues allow five deliveries, so a throttled
/// note could burn five full attempts inside the window the service needed to recover from the
/// first one.
/// </summary>
[TestFixture]
public class RedeliveryBackoffTests
{
    [Test]
    public void FirstDelivery_WaitsTheBaseDelay()
    {
        Assert.That(RedeliveryBackoff.DelayFor(1), Is.EqualTo(TimeSpan.FromSeconds(5)));
    }

    [TestCase(1, 5)]
    [TestCase(2, 10)]
    [TestCase(3, 20)]
    [TestCase(4, 40)]
    public void DelayDoublesPerAttempt(long deliveryCount, double expectedSeconds)
    {
        Assert.That(RedeliveryBackoff.DelayFor(deliveryCount).TotalSeconds, Is.EqualTo(expectedSeconds));
    }

    [TestCase(5)]
    [TestCase(6)]
    [TestCase(50)]
    public void FinalDelivery_DoesNotWaitAtAll(long deliveryCount)
    {
        // Abandoning on the last allowed delivery dead-letters the message rather than
        // redelivering it, so waiting first only holds the worker's single slot while a message
        // with no retries left is discarded. During a sustained outage with a full queue that was
        // a wasted minute per message, added to the outage.
        Assert.That(RedeliveryBackoff.DelayFor(deliveryCount), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TotalBackoffAcrossTheQueuesDeliveryLimit_IsMinutesNotMilliseconds()
    {
        // MaxDeliveryCount is 5 on both production queues. This is the number that matters: how
        // long a sustained outage is given to resolve before the message dead-letters. The fifth
        // delivery contributes nothing, because it is the one that dead-letters.
        var total = Enumerable.Range(1, RedeliveryBackoff.QueueMaxDeliveryCount)
            .Sum(i => RedeliveryBackoff.DelayFor(i).TotalSeconds);

        Assert.That(total, Is.EqualTo(75), "5 + 10 + 20 + 40, then nothing before dead-lettering");
        Assert.That(TimeSpan.FromSeconds(total), Is.GreaterThan(TimeSpan.FromMinutes(1)));
    }

    [Test]
    public void NoSingleDelayCanExhaustTheLockOnItsOwn()
    {
        // Guards the delay in isolation, NOT the cumulative budget. What actually risks a lapsed
        // lock is handler time PLUS this delay against MaxAutoLockRenewalDuration — a handwriting
        // source can spend ~4 minutes in the handler before the backoff is even reached. That is
        // handled by tolerating MessageLockLost on abandon rather than by shrinking this number.
        for (long attempt = 1; attempt <= 20; attempt++)
        {
            Assert.That(RedeliveryBackoff.DelayFor(attempt), Is.LessThanOrEqualTo(TimeSpan.FromSeconds(60)),
                $"attempt {attempt} must not wait longer than the cap");
        }
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void NonPositiveDeliveryCount_DoesNotProduceANonsenseDelay(long deliveryCount)
    {
        // DeliveryCount should be 1 on first delivery, but a negative exponent would yield a
        // sub-second delay that quietly defeats the whole mechanism.
        Assert.That(RedeliveryBackoff.DelayFor(deliveryCount), Is.EqualTo(TimeSpan.FromSeconds(5)));
    }
}
