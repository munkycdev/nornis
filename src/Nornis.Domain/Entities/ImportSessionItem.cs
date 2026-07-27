namespace Nornis.Domain.Entities;

/// <summary>
/// One note in an <see cref="ImportSession"/>'s backlog. Carries no status of its own: the
/// item's state is read off its source (Draft = waiting, Queued/Processing = extracting,
/// Processed with open proposals = reviewing, and so on). Only <see cref="Skipped"/> is a
/// decision the walk records here, because nothing about the source expresses it.
/// </summary>
public class ImportSessionItem
{
    public Guid Id { get; set; }

    public Guid ImportSessionId { get; set; }

    /// <summary>
    /// The note this item walks. A loose reference (no FK) like
    /// <see cref="ExtractionReplay.CurrentSourceId"/>: a Source FK alongside the session's
    /// World cascade would create competing cascade paths.
    /// </summary>
    public Guid SourceId { get; set; }

    /// <summary>Sort key for the walk, ascending. Gaps are allowed — deletes do not renumber.</summary>
    public int Position { get; set; }

    /// <summary>Passed over by the GM: the walk moves on without waiting for this note.</summary>
    public bool Skipped { get; set; }

    // Navigation properties
    public ImportSession ImportSession { get; set; } = null!;
}
