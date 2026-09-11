using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// A play-context within a world: a named run of sessions. Deliberately thin —
/// campaigns carry no membership and no permissions; world membership governs access.
/// </summary>
public class Campaign : ISlugged
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Per-world URL slug; see <see cref="ISlugged"/>.</summary>
    public string? Slug { get; set; }

    string ISlugged.SlugSource => Name;

    public string? Description { get; set; }

    public CampaignStatus Status { get; set; }

    /// <summary>
    /// GM-chosen display position, ascending, tie-broken by newest-first. Governs the
    /// settings list, the campaign index and the Sources filter — <em>not</em> the storyline
    /// timeline, whose campaign bands are ordered chronologically by their effective start
    /// and must stay that way.
    ///
    /// Zero means this world has never been reordered: reorder writes every row in the world
    /// at once with 1-based positions, so a zero is never one campaign's position among
    /// assigned siblings. A world still at all-zeroes falls back to newest-first, which is
    /// the order that predates this column.
    /// </summary>
    public int SortOrder { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;

    public User CreatedByUser { get; set; } = null!;

    public ICollection<CampaignCharacter> CampaignCharacters { get; set; } = [];
}
