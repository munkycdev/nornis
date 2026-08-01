using Nornis.Application.Services;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The monitor is what stands in for an active Azure OpenAI probe, so what matters is that
/// it forgets on the right schedule: old traffic must not keep a recovered provider looking
/// broken, and a bounded ring must not lose the recent past while wrapping.
/// </summary>
[TestFixture]
public class AiOutcomeMonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    [Test]
    public void Snapshot_WithNothingRecorded_IsIdle()
    {
        var snapshot = new AiOutcomeMonitor().Snapshot(Window, Now);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Total, Is.Zero);
            Assert.That(snapshot.Failures, Is.Zero);
            Assert.That(snapshot.LastAt, Is.Null);
        });
    }

    [Test]
    public void Snapshot_CountsOnlyOutcomesInsideTheWindow()
    {
        var monitor = new AiOutcomeMonitor();
        monitor.Record(succeeded: false, Now.AddMinutes(-20));
        monitor.Record(succeeded: true, Now.AddMinutes(-5));

        var snapshot = monitor.Snapshot(Window, Now);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Total, Is.EqualTo(1));
            Assert.That(snapshot.Failures, Is.Zero);
        });
    }

    [Test]
    public void Snapshot_ReportsLastOutcomeEvenWhenItPredatesTheWindow()
    {
        var monitor = new AiOutcomeMonitor();
        monitor.Record(succeeded: true, Now.AddHours(-3));

        var snapshot = monitor.Snapshot(Window, Now);

        Assert.Multiple(() =>
        {
            // Idle to the check, but the page can still say when the provider last answered.
            Assert.That(snapshot.Total, Is.Zero);
            Assert.That(snapshot.LastAt, Is.EqualTo(Now.AddHours(-3)));
        });
    }

    [Test]
    public void Snapshot_SeparatesFailuresFromSuccesses()
    {
        var monitor = new AiOutcomeMonitor();
        monitor.Record(succeeded: true, Now.AddMinutes(-3));
        monitor.Record(succeeded: false, Now.AddMinutes(-2));
        monitor.Record(succeeded: false, Now.AddMinutes(-1));

        var snapshot = monitor.Snapshot(Window, Now);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Total, Is.EqualTo(3));
            Assert.That(snapshot.Failures, Is.EqualTo(2));
        });
    }

    [Test]
    public void Snapshot_AfterWrappingTheRing_ReflectsOnlyTheMostRecentCalls()
    {
        var monitor = new AiOutcomeMonitor();

        // A burst of failures, then a full ring's worth of successes on top: the failures
        // are older than the ring is deep, so nothing should remember them.
        for (var i = 0; i < 5; i++)
        {
            monitor.Record(succeeded: false, Now.AddMinutes(-10));
        }

        for (var i = 0; i < AiOutcomeMonitor.Capacity; i++)
        {
            monitor.Record(succeeded: true, Now.AddMinutes(-1));
        }

        var snapshot = monitor.Snapshot(Window, Now);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Total, Is.EqualTo(AiOutcomeMonitor.Capacity));
            Assert.That(snapshot.Failures, Is.Zero);
        });
    }
}
