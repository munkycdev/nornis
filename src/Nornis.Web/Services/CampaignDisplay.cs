using MudBlazor;
using Nornis.Web.ApiClient;

namespace Nornis.Web.Services;

/// <summary>
/// How a campaign reads on a page — the member page and the public one say the same things
/// about the same run of play, so the words and the grouping live here and not in either.
/// </summary>
public static class CampaignDisplay
{
    /// <summary>
    /// "Played 4 Jan to 22 Mar 2026 · 9 sessions". Declared dates first — they are what the GM
    /// typed — falling back to the span the sessions actually cover. The campaigns list and the
    /// campaign page both read this, so they cannot disagree: until 3.32 the list had its own
    /// line that knew only the typed dates and said "No dates set" beside a campaign whose page
    /// showed five dated sessions.
    /// </summary>
    public static string PlayedSpan(CampaignDto campaign, DateTimeOffset? firstSessionAt, DateTimeOffset? lastSessionAt, int sessionCount)
    {
        var declared = (campaign.StartedAt, campaign.EndedAt) switch
        {
            ({ } s, { } e) => $"Played {DateDisplay.Span(s, e)}",
            ({ } s, null) => $"Began {DateDisplay.Day(s)}",
            (null, { } e) => $"Ended {DateDisplay.Day(e)}",
            _ => null
        };

        var observed = (firstSessionAt, lastSessionAt) switch
        {
            ({ } f, { } l) => DateDisplay.Span(f, l),
            ({ } f, _) => DateDisplay.Day(f),
            _ => null
        };

        var sessions = sessionCount switch
        {
            0 => "No sessions yet",
            1 => "1 session",
            _ => $"{sessionCount} sessions",
        };

        return (declared ?? observed) is { } span ? $"{span} · {sessions}" : sessions;
    }

    /// <summary>What a campaign's sources say about when it was played, for a list that has no detail to read.</summary>
    public sealed record SessionSpan(DateTimeOffset? First, DateTimeOffset? Last, int Count);

    /// <summary>
    /// The span of each campaign's sources, from the sources list the reader may see. Counts every
    /// source filed under the campaign and dates the span by those that carry a date, which is
    /// what the campaign page's own figures are.
    /// </summary>
    public static IReadOnlyDictionary<Guid, SessionSpan> SessionSpans(IEnumerable<SourceListItem> sources) =>
        sources
            .Where(s => s.CampaignId is not null)
            .GroupBy(s => s.CampaignId!.Value)
            .ToDictionary(
                g => g.Key,
                g => new SessionSpan(g.Min(s => s.OccurredAt), g.Max(s => s.OccurredAt), g.Count()));


    /// <summary>
    /// The title both campaign pages put over the recap. "Recap" is the one word for the
    /// AI-written summary since 3.32: Home's card is the world recap, this is the campaign's.
    /// </summary>
    public const string RecapTitle = "Campaign recap";

    /// <summary>
    /// The heading the recap prompt opens its text with, which <see cref="RecapBody"/> drops
    /// because the card already has a title. It was the card's title too until 3.32.
    /// </summary>
    private const string RecapLeadHeading = "The story so far";

    /// <summary>
    /// The recap body as the page shows it. The prompt writes the recap in three headed
    /// sections, and the first is headed with the same words the page puts over the card — so
    /// that one heading is lifted off, and the other two stay as the structure of the text.
    /// Matches the heading by its words, ignoring the level and anything after an em-dash.
    /// </summary>
    public static string RecapBody(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var text = markdown.TrimStart();
        var lineEnd = text.IndexOf('\n');
        var firstLine = (lineEnd < 0 ? text : text[..lineEnd]).TrimEnd('\r');
        var heading = firstLine.TrimStart('#');
        if (heading.Length == firstLine.Length)
        {
            return markdown;
        }

        var dash = heading.IndexOf('—');
        var words = (dash < 0 ? heading : heading[..dash]).Trim();
        if (!string.Equals(words, RecapLeadHeading, StringComparison.OrdinalIgnoreCase))
        {
            return markdown;
        }

        return lineEnd < 0 ? string.Empty : text[(lineEnd + 1)..].TrimStart('\r', '\n');
    }

    public static Color StatusColor(string status) => status switch
    {
        "Active" => Color.Success,
        "Completed" => Color.Info,
        _ => Color.Default
    };

    public sealed record ArtifactGroup(string Label, IReadOnlyList<CampaignArtifactDto> Items);

    /// <summary>
    /// Grouped by type, most-populated group first, so a campaign's dominant shape — a cast of
    /// characters, or a tour of places — leads. Order within a group is the server's ranking
    /// (most-cited first) and is left alone.
    /// </summary>
    public static IReadOnlyList<ArtifactGroup> Group(IReadOnlyList<CampaignArtifactDto> artifacts) =>
        artifacts
            .GroupBy(a => a.Type)
            .Select(g => new ArtifactGroup(ArtifactTypeDisplay.Plural(g.Key), g.ToList()))
            .OrderByDescending(g => g.Items.Count)
            .ThenBy(g => g.Label)
            .ToList();
}
