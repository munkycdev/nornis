using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

/// <summary>
/// One date format since 3.32. The same day read "11/12/2026" in a picker, "12 Nov 2026" in a
/// list, "2026-10-12" on a campaign page and "Nov 12, 2026" on the map.
/// </summary>
[TestFixture]
public class DateDisplayTests
{
    [Test]
    public void ADay_IsDayFirstWithTheMonthAsAWord()
    {
        Assert.That(DateDisplay.Day(new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero)), Is.EqualTo("12 Nov 2026"));
    }

    [Test]
    public void ASessionDate_ShowsTheDayThatWasPicked_WhateverZoneTheServerIsIn()
    {
        // A session date is stored as midnight UTC of the picked day. Read in the server's own
        // zone, that is the evening before anywhere west of Greenwich, which is every developer
        // machine in the Americas. The value below is the same instant written in such a zone.
        var pickedThe12th = new DateTimeOffset(2026, 11, 11, 16, 0, 0, TimeSpan.FromHours(-8));

        Assert.That(DateDisplay.Day(pickedThe12th), Is.EqualTo("12 Nov 2026"));
    }

    [Test]
    public void NoDate_IsNothing_NotTheYearOne()
    {
        Assert.That(DateDisplay.Day((DateTimeOffset?)null), Is.Null);
    }

    [Test]
    public void ADayWithItsTime_UsesTheSameDay()
    {
        Assert.That(DateDisplay.DayAndTime(new DateTimeOffset(2026, 11, 12, 14, 5, 0, TimeSpan.Zero)), Is.EqualTo("12 Nov 2026, 14:05"));
    }

    [TestCase(2026, 10, 12, 2026, 11, 9, "12 Oct to 9 Nov 2026")]
    [TestCase(2025, 10, 8, 2026, 3, 1, "8 Oct 2025 to 1 Mar 2026")]
    [TestCase(2026, 11, 12, 2026, 11, 12, "12 Nov 2026")]
    public void ASpan_SaysTheYearOnce_UnlessItCrossesOne(int y1, int m1, int d1, int y2, int m2, int d2, string expected)
    {
        var from = new DateTimeOffset(y1, m1, d1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(y2, m2, d2, 0, 0, 0, TimeSpan.Zero);

        Assert.That(DateDisplay.Span(from, to), Is.EqualTo(expected));
    }
}
