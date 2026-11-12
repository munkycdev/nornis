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

    /// <summary>
    /// Dollars as a person reads them: two decimals, and "under $0.01" for a spend too small
    /// to show in two. The Costs tab used four decimals below a dollar, so a new world's cards
    /// read "$0.0000" (3.32).
    /// </summary>
    public static string Money(decimal usd) => usd switch
    {
        > 0 and < 0.005m => "under $0.01",
        _ => usd.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US")),
    };

    /// <summary>
    /// What a Copy button says when the browser will not copy (permission refused). Every place
    /// that offers one also shows the link, so the way out is to select it.
    /// </summary>
    public const string CopyRefused = "Your browser would not copy that. Select the link and copy it yourself.";

    /// <summary>
    /// The confidence below which a reader is told about it. Above it the figure is noise:
    /// "97 % confidence" on nearly every row says nothing, so only doubt is shown (3.32).
    /// </summary>
    public const decimal LowConfidenceBelow = 0.6m;

    /// <summary>"Low confidence (45%)" when the extraction was unsure, otherwise nothing.</summary>
    public static string? LowConfidence(decimal? confidence) =>
        confidence is { } c && c < LowConfidenceBelow ? $"Low confidence ({Math.Round(c * 100, MidpointRounding.AwayFromZero):0}%)" : null;

    /// <summary>PascalCase identifiers as words: "PartyVisible" → "Party Visible".</summary>
    public static string Humanize(string pascal) =>
        string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));

    /// <summary>
    /// A relationship type as it reads in a sentence: "LocatedIn" → "located in", "PartOf" →
    /// "part of". Relationship types are free text the extraction proposes, so anything already
    /// written as words passes through lower-cased.
    /// </summary>
    public static string Predicate(string type) =>
        System.Text.RegularExpressions.Regex.Replace(type.Trim(), "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();

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
