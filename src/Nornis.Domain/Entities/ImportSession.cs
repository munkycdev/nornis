using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// A GM's campaign backlog, walked one note at a time. The whole backlog is pasted in up
/// front and ordered; then each note is extracted against the vetted canon its predecessors
/// established, rather than every note racing against the same stale canon in parallel.
/// Notes not yet reached are simply Sources held at
/// <see cref="SourceProcessingStatus.Draft"/>; advancing marks the next one ready.
/// At most one non-terminal (Draft or InProgress) session exists per world.
/// </summary>
public class ImportSession
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    /// <summary>The GM running the import; notes are created and marked ready on their behalf.</summary>
    public Guid CreatedByUserId { get; set; }

    public ImportSessionStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;

    public ICollection<ImportSessionItem> Items { get; set; } = [];
}
