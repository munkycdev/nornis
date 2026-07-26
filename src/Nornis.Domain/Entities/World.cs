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

    /// <summary>
    /// True for worlds instantiated from the demo template. Demo worlds are excluded from
    /// usage metrics and can be cut off from public access wholesale via the
    /// DemoWorlds:PublicAccessEnabled kill switch.
    /// </summary>
    public bool IsDemo { get; set; }

    /// <summary>
    /// Whether this demo world was created with the guided tutorial. Only meaningful when
    /// <see cref="IsDemo"/> is true; the tutorial UI (feature 20 phase C) keys off it.
    /// </summary>
    public bool TutorialEnabled { get; set; }

    /// <summary>
    /// True for a hand-authored master world that exists to be exported as a template
    /// package. Purely a UI grouping hint — the world switcher files these under a
    /// "Templates" section so they stay reachable without cluttering the everyday list.
    /// The opposite of <see cref="IsDemo"/>, which marks a world instantiated *from* a
    /// template; nothing in the demo clone path reads this flag.
    /// </summary>
    public bool IsTemplate { get; set; }

    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public User CreatedByUser { get; set; } = null!;

    public ICollection<WorldMember> WorldMembers { get; set; } = [];

    public ICollection<Campaign> Campaigns { get; set; } = [];
}
