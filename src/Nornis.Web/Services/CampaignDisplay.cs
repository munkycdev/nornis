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
    /// "Played 2026-01-04 – 2026-03-22 · 9 sessions". Declared dates first — they are what the GM
    /// typed — falling back to the span the sessions actually cover.
    /// </summary>
    public static string PlayedSpan(CampaignDto campaign, DateTimeOffset? firstSessionAt, DateTimeOffset? lastSessionAt, int sessionCount)
    {
        var declared = (campaign.StartedAt, campaign.EndedAt) switch
        {
            ({ } s, { } e) => $"Played {s:yyyy-MM-dd} – {e:yyyy-MM-dd}",
            ({ } s, null) => $"Began {s:yyyy-MM-dd}",
            (null, { } e) => $"Ended {e:yyyy-MM-dd}",
            _ => null
        };

        var observed = (firstSessionAt, lastSessionAt) switch
        {
            ({ } f, { } l) when f != l => $"{f:yyyy-MM-dd} – {l:yyyy-MM-dd}",
            ({ } f, _) => $"{f:yyyy-MM-dd}",
            _ => null
        };

        var sessions = sessionCount == 1 ? "1 session" : $"{sessionCount} sessions";

        return (declared ?? observed) is { } span ? $"{span} · {sessions}" : sessions;
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
