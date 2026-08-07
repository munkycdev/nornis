namespace Nornis.Web.Services;

/// <summary>
/// The calendar axis shared by the storyline timeline and the journey scrubber. Both draw the
/// same sessions against the same dates, and both had written this themselves — the month-tick
/// loop was identical in the two files, which is the shape of bug that survives review because
/// each copy is correct on its own.
///
/// What is deliberately NOT here: how far each chart pads its range. The timeline pads a fixed
/// number of days, the journey pads a proportion of its span, and those are different answers
/// to different questions rather than a duplicate waiting to be merged.
/// </summary>
public static class TimeAxis
{
    /// <summary>
    /// The first of each month strictly inside the range. Starts at the month after
    /// <paramref name="min"/>'s, so a tick never lands on the axis's own edge.
    /// </summary>
    public static IEnumerable<DateTimeOffset> MonthTicks(DateTimeOffset min, DateTimeOffset max)
    {
        var tick = new DateTimeOffset(min.Year, min.Month, 1, 0, 0, 0, min.Offset).AddMonths(1);
        while (tick <= max)
        {
            yield return tick;
            tick = tick.AddMonths(1);
        }
    }

    /// <summary>
    /// Days across the range, floored at one. The floor is what keeps a single-session world —
    /// where min and max collapse to the same instant — from dividing by zero downstream.
    /// </summary>
    public static double SpanDays(DateTimeOffset min, DateTimeOffset max) =>
        Math.Max(1, (max - min).TotalDays);

    /// <summary>
    /// Where a date sits across the range, 0–100, clamped. A range with no width puts
    /// everything at the midpoint: with one session there is no "along" to be part of, and the
    /// centre is the only honest answer.
    /// </summary>
    public static double Percent(DateTimeOffset date, DateTimeOffset min, DateTimeOffset max)
    {
        var total = (max - min).TotalDays;
        if (total <= 0)
        {
            return 50;
        }

        return Math.Clamp((date - min).TotalDays / total * 100.0, 0, 100);
    }
}
