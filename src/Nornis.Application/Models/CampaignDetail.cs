using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Models;

/// <summary>
/// Everything the campaign page reads, assembled for one caller at their visibility.
///
/// Every collection here is derived or declared elsewhere — the campaign itself owns only
/// its name, dates, status and the GM's written intro. That is the whole point: a campaign
/// is a first-tier place to look, not a second place for knowledge to live.
/// </summary>
public record CampaignDetail(
    Campaign Campaign,
    IReadOnlyList<Character> Characters,
    CampaignRollup Rollup,
    IReadOnlyList<SourceListItem> RecentSessions,
    int SessionCount,
    DateTimeOffset? FirstSessionAt,
    DateTimeOffset? LastSessionAt,
    CampaignRecapView Recap,
    UnfiledSources UnfiledInSpan);

/// <summary>
/// Sources filed under no campaign whose events fall inside this campaign's declared dates —
/// what the page offers the GM to file here. Empty for every reader but a GM (only a GM can
/// act on it) and for a campaign with no dates (there is no span to fall inside).
/// <paramref name="TotalCount"/> is how many there are; <paramref name="Items"/> is the
/// first <see cref="Services.CampaignService.MaxUnfiledOffered"/> of them, so a long
/// backlog is filed in rounds rather than painted onto one page.
/// </summary>
public sealed record UnfiledSources(IReadOnlyList<SourceListItem> Items, int TotalCount)
{
    public static readonly UnfiledSources None = new([], 0);
}

/// <summary>
/// The generated recap rendered for one reader. Mirrors <c>WorldDigestView</c>, including
/// its HasData=false convention for "never generated".
/// </summary>
public sealed record CampaignRecapView(
    bool HasData,
    DateTimeOffset? GeneratedAt,
    string? Content,
    string? PartyPreview)
{
    /// <summary>
    /// The one place a stored recap becomes a reader's view. GMs get their rendering plus
    /// the party preview, so a GM can see what the players' page says; everyone else gets
    /// the party rendering alone and never learns the GM one exists.
    /// </summary>
    public static CampaignRecapView From(CampaignRecap? recap, WorldRole actingRole)
    {
        if (recap is null)
        {
            return new CampaignRecapView(false, null, null, null);
        }

        return actingRole == WorldRole.GM
            ? new CampaignRecapView(true, recap.GeneratedAt, recap.GmContentMarkdown, recap.PartyContentMarkdown)
            : new CampaignRecapView(true, recap.GeneratedAt, recap.PartyContentMarkdown, null);
    }
}
