namespace Nornis.Domain.Entities;

/// <summary>
/// Join declaring that a storyline (an <see cref="Artifact"/> of type Storyline) belongs to a
/// campaign. A storyline is world-scoped and may thread through several campaigns, so it can
/// carry more than one of these; (ArtifactId, CampaignId) is unique.
///
/// This is the GM-curated, declared membership — deliberately distinct from the campaigns
/// merely <em>derived</em> from the sessions that touched a storyline. The timeline unions the
/// two; a declaration lets a GM place a storyline in a campaign before any session there
/// references it.
/// </summary>
public class StorylineCampaign
{
    public Guid Id { get; set; }

    /// <summary>The storyline artifact. Enforced to be <c>ArtifactType.Storyline</c> at the service layer.</summary>
    public Guid ArtifactId { get; set; }

    public Guid CampaignId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The GM who declared the membership. Null for links created outside a user context.</summary>
    public Guid? CreatedByUserId { get; set; }

    // Navigation properties
    public Artifact Artifact { get; set; } = null!;

    public Campaign Campaign { get; set; } = null!;
}
