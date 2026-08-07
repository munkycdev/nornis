using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class WorldMember
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public Guid UserId { get; set; }

    public WorldRole Role { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>
    /// How far this member has read the world's reveals. Null means they have never looked,
    /// which is not the same as having seen nothing — a member who joins a world with two years
    /// of disclosures behind it gets a bounded first view rather than all of them.
    ///
    /// Server-side rather than in the browser: the same person reads on a phone at the table
    /// and a laptop afterwards, and this codebase has been bitten once already by keeping
    /// reader state in local storage.
    /// </summary>
    public DateTimeOffset? LearnedSeenAt { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;

    public User User { get; set; } = null!;

    public ICollection<Character> Characters { get; set; } = [];
}
