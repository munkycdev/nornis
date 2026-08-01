namespace Nornis.Web.Services;

/// <summary>Display-side string helpers shared across pages and panels.</summary>
public static class DisplayText
{
    /// <summary>
    /// Ellipsis truncation for display: the result never exceeds
    /// <paramref name="maxLength"/> characters, ellipsis included. Deliberate mirror of
    /// Application's <c>StringExtensions.Truncate</c> — Web deploys against the API and
    /// shares no assembly with the backend.
    /// </summary>
    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)].TrimEnd() + "…";
}
