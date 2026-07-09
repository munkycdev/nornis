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

    // Navigation properties
    public World World { get; set; } = null!;

    public User User { get; set; } = null!;

    public ICollection<Character> Characters { get; set; } = [];
}
