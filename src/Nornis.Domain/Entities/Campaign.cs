using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// A play-context within a world: a named run of sessions. Deliberately thin —
/// campaigns carry no membership and no permissions; world membership governs access.
/// </summary>
public class Campaign
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public CampaignStatus Status { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;

    public User CreatedByUser { get; set; } = null!;

    public ICollection<CampaignCharacter> CampaignCharacters { get; set; } = [];

    public ICollection<StorylineCampaign> StorylineCampaigns { get; set; } = [];
}
