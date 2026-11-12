namespace Nornis.Domain.Entities;

/// <summary>
/// One round of questions Nornis put to the GM about what it is tracking (feature 44). It holds
/// every question the evidence produced: the first few are asked on Home, the rest wait on the
/// Check-in page as the longer list. A world has at most one open check-in; starting another
/// closes it.
/// </summary>
public class CheckIn
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    /// <summary>The model that chose and phrased the asked questions; empty when no call was made.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// The latest dated session the check-in saw, and its date: the cadence anchor. The next
    /// check-in is due once the configured number of dated sessions carry a later date. Null
    /// when the world had no dated session yet.
    /// </summary>
    public Guid? LatestSessionSourceId { get; set; }

    public DateTimeOffset? LatestSessionAt { get; set; }

    /// <summary>Set when a later check-in replaced this one. Open while null.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;

    public ICollection<CheckInQuestion> Questions { get; set; } = [];
}
