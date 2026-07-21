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

    /// <summary>GM-chosen URL slug for public read-only access (nornis.app/w/{slug}).
    /// Kept when access is toggled off, so re-enabling restores the same link.</summary>
    public string? PublicSlug { get; set; }

    /// <summary>Gate for anonymous read-only access to party-visible knowledge. Default off.</summary>
    public bool PublicAccessEnabled { get; set; }

    /// <summary>
    /// GM-configured monthly spend cap (USD) for anonymous "Ask the Loremaster" on the public
    /// site. This value is also the on/off switch: null or ≤ 0 means public Ask is disabled
    /// (the safe default). A positive value enables it, capped at that much AI spend per
    /// calendar month. Independent of <see cref="DailyAiBudgetUsd"/>, which still applies as a
    /// backstop; public Ask is allowed only when both budgets have room.
    /// </summary>
    public decimal? PublicAskMonthlyBudgetUsd { get; set; }

    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public User CreatedByUser { get; set; } = null!;

    public ICollection<WorldMember> WorldMembers { get; set; } = [];

    public ICollection<Campaign> Campaigns { get; set; } = [];
}
