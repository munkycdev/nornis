using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class CampaignMember
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    public Guid UserId { get; set; }

    public CampaignRole Role { get; set; }

    public string? DisplayName { get; set; }

    public string? CharacterName { get; set; }

    public DateTimeOffset JoinedAt { get; set; }

    // Navigation properties
    public Campaign Campaign { get; set; } = null!;

    public User User { get; set; } = null!;
}
