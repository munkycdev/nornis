namespace Nornis.Application.Common;

public static class StringExtensions
{
    /// <summary>
    /// The one truncation. Ten private copies used to exist with two silent semantics —
    /// a raw cut and an ellipsis cut — so the choice is now explicit at the call site.
    /// With <paramref name="ellipsis"/> the result never exceeds
    /// <paramref name="maxLength"/> characters, ellipsis included.
    /// </summary>
    public static string Truncate(this string value, int maxLength, bool ellipsis = false)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return ellipsis
            ? value[..(maxLength - 1)].TrimEnd() + "…"
            : value[..maxLength];
    }

    /// <summary>
    /// A caller-supplied value made safe to write into a log: carriage returns and line feeds
    /// are removed, so a crafted value can't start a line that reads as a separate entry.
    /// Use it on anything a request can set (a query string, a file name, a content type)
    /// before logging it; ids, numbers and enums need nothing.
    /// </summary>
    public static string? ForLog(this string? value) =>
        value?.Replace("\r", string.Empty).Replace("\n", string.Empty);
}
