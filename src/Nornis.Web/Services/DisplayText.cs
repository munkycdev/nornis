using MudBlazor;

namespace Nornis.Web.Services;

/// <summary>Display-side helpers shared across pages and panels.</summary>
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

    /// <summary>PascalCase identifiers as words: "PartyVisible" → "Party Visible".</summary>
    public static string Humanize(string pascal) =>
        string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));

    /// <summary>
    /// Byte counts for humans. The GB tier is load-bearing: four copies stopped at MB
    /// and rendered "1228.8 MB" where the list said "1.2 GB".
    /// </summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };

    /// <summary>Continuity-health score bands, shared by Home and World Memory.</summary>
    public static Color HealthColor(int score) => score switch
    {
        >= 70 => Color.Success,
        >= 50 => Color.Warning,
        _ => Color.Error,
    };
}
