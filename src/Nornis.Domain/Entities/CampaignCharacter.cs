namespace Nornis.Domain.Entities;

/// <summary>
/// Join between a campaign and a character: the character is (or was) part of that
/// campaign. (CampaignId, CharacterId) is unique.
/// </summary>
public class CampaignCharacter
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    public Guid CharacterId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public Campaign Campaign { get; set; } = null!;

    public Character Character { get; set; } = null!;
}
