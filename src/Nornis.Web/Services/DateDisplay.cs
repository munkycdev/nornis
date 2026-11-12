using System.Globalization;

namespace Nornis.Web.Services;

/// <summary>
/// How a date reads everywhere a person reads one: "12 Nov 2026". One place since 3.32, when
/// the same day showed as "11/12/2026" in a picker, "12 Nov 2026" in a list, "2026-10-12" on a
/// campaign page and "Nov 12, 2026" on the map, which in a product used on both sides of the
/// Atlantic left "11/12" meaning two different days. Day first, month as a word, so it cannot be
/// misread. Machine-facing dates (file names, exports, query strings) are not this and keep ISO.
///
/// Every value is read in UTC. A session date is a calendar day, stored as midnight UTC of the
/// day that was picked, so reading it in the server's own zone shows the day before anywhere
/// west of Greenwich; the pages run on the server, which has no idea of the reader's zone, and
/// in production the server's zone is UTC anyway. Reading UTC makes the day shown the day
/// picked, on every machine.
/// </summary>
public static class DateDisplay
{
    /// <summary>The format string, for a control that takes one (a date picker's DateFormat).</summary>
    public const string DayFormat = "d MMM yyyy";

    public static string Day(DateTimeOffset value) => value.UtcDateTime.ToString(DayFormat, CultureInfo.InvariantCulture);

    public static string Day(DateTime value) => value.ToString(DayFormat, CultureInfo.InvariantCulture);

    public static string? Day(DateTimeOffset? value) => value is { } v ? Day(v) : null;

    /// <summary>A day with its time, for when both matter: "12 Nov 2026, 14:05".</summary>
    public static string DayAndTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"12 Oct to 9 Nov 2026", or with both years when they differ.</summary>
    public static string Span(DateTimeOffset from, DateTimeOffset to)
    {
        var (a, b) = (from.UtcDateTime, to.UtcDateTime);
        if (a.Date == b.Date)
        {
            return Day(a);
        }

        var start = a.Year == b.Year
            ? a.ToString("d MMM", CultureInfo.InvariantCulture)
            : Day(a);
        return $"{start} to {Day(b)}";
    }
}
