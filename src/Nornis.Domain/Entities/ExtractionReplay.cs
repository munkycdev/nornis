using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// A GM-initiated re-extraction of the world's timeline, one source at a time in story
/// order. Only the cursor source is ever in flight: the replay advances to the next
/// eligible source when the cursor's review batch is fully resolved (or its extraction
/// yields nothing to review), so each note is extracted against the knowledge its
/// predecessors established — locations and themes land in their place in time.
/// At most one Active replay exists per world.
/// </summary>
public class ExtractionReplay
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    /// <summary>
    /// The source currently queued, processing, or awaiting review. A loose reference
    /// (no FK) like <see cref="MapPlacemark.ArtifactId"/> — a Source FK alongside the
    /// World cascade would create competing cascade paths. If the cursor source is
    /// deleted mid-replay the replay simply stalls until canceled.
    /// </summary>
    public Guid CurrentSourceId { get; set; }

    public ExtractionReplayStatus Status { get; set; }

    /// <summary>The GM who started the replay; advances reprocess sources on their behalf.</summary>
    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Concurrency token: two racing advances must not both claim the cursor —
    /// the loser's update throws instead of double-queueing a source.</summary>
    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public World World { get; set; } = null!;
}
