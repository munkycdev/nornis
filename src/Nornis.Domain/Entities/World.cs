namespace Nornis.Domain.Entities;

public class World
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? GameSystem { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    /// <summary>
    /// Per-world daily AI budget override in USD; when null the server-configured
    /// default applies.
    /// </summary>
    public decimal? DailyAiBudgetUsd { get; set; }

    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public User CreatedByUser { get; set; } = null!;

    public ICollection<WorldMember> WorldMembers { get; set; } = [];

    public ICollection<Campaign> Campaigns { get; set; } = [];
}
