using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

/// <summary>
/// The calendar axis both the storyline timeline and the journey scrubber draw against. It had
/// no tests while it lived twice inside two Razor components; the edges below are the ones a
/// one-session world and an empty one actually hit.
/// </summary>
[TestFixture]
public class TimeAxisTests
{
    private static readonly DateTimeOffset Jan15 = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    #region MonthTicks

    [Test]
    public void MonthTicks_StartAfterTheRangeBeginsSoNoTickSitsOnTheEdge()
    {
        var ticks = TimeAxis.MonthTicks(Jan15, Jan15.AddMonths(3)).ToList();

        Assert.That(ticks.Select(t => (t.Year, t.Month)), Is.EqualTo(
            [(2026, 2), (2026, 3), (2026, 4)]).AsCollection);
    }

    [Test]
    public void MonthTicks_AreEmptyWhenTheRangeDoesNotCrossAMonthBoundary()
    {
        var ticks = TimeAxis.MonthTicks(Jan15, Jan15.AddDays(5));

        Assert.That(ticks, Is.Empty);
    }

    [Test]
    public void MonthTicks_AreEmptyForACollapsedRange()
    {
        // One session: min and max are the same instant. The loop must terminate rather than
        // yield the same tick forever.
        Assert.That(TimeAxis.MonthTicks(Jan15, Jan15), Is.Empty);
    }

    [Test]
    public void MonthTicks_AreEmptyWhenTheRangeIsInverted()
    {
        Assert.That(TimeAxis.MonthTicks(Jan15, Jan15.AddMonths(-3)), Is.Empty);
    }

    [Test]
    public void MonthTicks_CrossAYearBoundary()
    {
        var ticks = TimeAxis.MonthTicks(new DateTimeOffset(2025, 11, 20, 0, 0, 0, TimeSpan.Zero), Jan15).ToList();

        Assert.That(ticks.Select(t => (t.Year, t.Month)), Is.EqualTo(
            [(2025, 12), (2026, 1)]).AsCollection);
    }

    #endregion

    #region SpanDays

    [Test]
    public void SpanDays_MeasuresTheRange()
    {
        Assert.That(TimeAxis.SpanDays(Jan15, Jan15.AddDays(30)), Is.EqualTo(30).Within(0.0001));
    }

    [TestCase(0)]
    [TestCase(-10)]
    public void SpanDays_FloorsAtOneSoNothingDividesByZero(int days)
    {
        Assert.That(TimeAxis.SpanDays(Jan15, Jan15.AddDays(days)), Is.EqualTo(1));
    }

    #endregion

    #region Percent

    [Test]
    public void Percent_IsMonotonicAcrossTheRange()
    {
        var min = Jan15;
        var max = Jan15.AddDays(100);

        var samples = Enumerable.Range(0, 21)
            .Select(i => TimeAxis.Percent(min.AddDays(i * 5), min, max))
            .ToList();

        Assert.That(samples.Zip(samples.Skip(1)).All(p => p.Second >= p.First), Is.True);
    }

    [Test]
    public void Percent_PutsTheEndsAtZeroAndOneHundred()
    {
        var min = Jan15;
        var max = Jan15.AddDays(100);

        Assert.Multiple(() =>
        {
            Assert.That(TimeAxis.Percent(min, min, max), Is.EqualTo(0).Within(0.0001));
            Assert.That(TimeAxis.Percent(max, min, max), Is.EqualTo(100).Within(0.0001));
            Assert.That(TimeAxis.Percent(min.AddDays(50), min, max), Is.EqualTo(50).Within(0.0001));
        });
    }

    [Test]
    public void Percent_ClampsADateOutsideTheRange()
    {
        var min = Jan15;
        var max = Jan15.AddDays(100);

        Assert.Multiple(() =>
        {
            Assert.That(TimeAxis.Percent(min.AddDays(-40), min, max), Is.EqualTo(0));
            Assert.That(TimeAxis.Percent(max.AddDays(40), min, max), Is.EqualTo(100));
        });
    }

    [Test]
    public void Percent_PutsACollapsedRangeAtTheMidpoint()
    {
        // With one session there is no "along" to be part of, and the centre is the only
        // honest answer — the alternative is a playhead pinned to an edge it did not earn.
        Assert.That(TimeAxis.Percent(Jan15, Jan15, Jan15), Is.EqualTo(50));
    }

    #endregion
}
