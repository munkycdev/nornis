namespace Nornis.Application.Services;

internal static class TimePeriodCalculator
{
    public static (DateTimeOffset Start, DateTimeOffset End) GetTodayRange()
    {
        var now = DateTimeOffset.UtcNow;
        var startOfDay = new DateTimeOffset(now.Date, TimeSpan.Zero);
        return (startOfDay, now);
    }

    public static (DateTimeOffset Start, DateTimeOffset End) GetThisWeekRange()
    {
        var now = DateTimeOffset.UtcNow;
        var today = now.Date;
        var daysSinceMonday = ((int)today.DayOfWeek - 1 + 7) % 7;
        var monday = today.AddDays(-daysSinceMonday);
        return (new DateTimeOffset(monday, TimeSpan.Zero), now);
    }

    public static (DateTimeOffset Start, DateTimeOffset End) GetThisMonthRange()
    {
        var now = DateTimeOffset.UtcNow;
        var firstOfMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return (firstOfMonth, now);
    }
}
