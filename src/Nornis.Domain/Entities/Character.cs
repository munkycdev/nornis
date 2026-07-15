namespace Nornis.Domain.Entities;

/// <summary>
/// A playable identity owned by a world member. A member may have any number of
/// characters, and a character may participate in any number of campaigns. Distinct
/// from the AI-extracted Artifact of type Character.
/// </summary>
public class Character
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public Guid WorldMemberId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Optional link to the AI-extracted Artifact (Type == Character) describing the
    /// same fictional person.
    /// </summary>
    public Guid? ArtifactId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;

    public WorldMember WorldMember { get; set; } = null!;

    public Artifact? Artifact { get; set; }

    public ICollection<CampaignCharacter> CampaignCharacters { get; set; } = [];
}
