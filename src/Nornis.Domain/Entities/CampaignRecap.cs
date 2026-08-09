namespace Nornis.Domain.Entities;

/// <summary>
/// A campaign's generated "story so far" — the campaign-scoped sibling of
/// <see cref="WorldDigest"/>, and a read-model for the same reason: it must never flow
/// through review or pollute the knowledge graph it summarizes, so it hangs off the
/// campaign as one replaceable row.
///
/// It lives here rather than as columns on <see cref="Campaign"/> because a campaign is
/// deliberately thin — a label and a timeline — and generation metadata is not part of
/// what a campaign <em>is</em>. The GM-written intro stays on <c>Campaign.Description</c>;
/// this is only the generated half.
///
/// Two renderings are one generation act, produced from separately-scoped passes and
/// replaced together: the campaign detail page is player-visible, so a row where the GM
/// half was newer than the party half would misstate what the players' version knows.
/// </summary>
public class CampaignRecap
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    /// <summary>The GM rendering: the campaign's full record, hidden truths included. GMOnly surface.</summary>
    public string GmContentMarkdown { get; set; } = string.Empty;

    /// <summary>
    /// The party rendering, generated from the Observer-floor view of the campaign's
    /// record — PartyVisible material only, no one's Private notes, no Hidden truth
    /// states — because it renders to every member of the world.
    /// </summary>
    public string PartyContentMarkdown { get; set; } = string.Empty;

    /// <summary>The model that generated this recap, snapshotted at generation time.</summary>
    public string Model { get; set; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; set; }

    public Guid GeneratedByUserId { get; set; }

    // Navigation property
    public Campaign Campaign { get; set; } = null!;
}
