namespace Nornis.Domain.Entities;

public class Campaign
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? GameSystem { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public User CreatedByUser { get; set; } = null!;

    public ICollection<CampaignMember> CampaignMembers { get; set; } = [];
}
