namespace Nornis.Domain.Entities;

/// <summary>
/// One note in an <see cref="ImportSession"/>'s backlog. The item's state is read off its
/// source (Queued/Processing = extracting, Processed with open proposals = reviewing, and
/// so on); what the item stores is only what the source cannot express — whether the GM
/// skipped it, whether this flow created it, and whether the walk has sent it yet.
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

    /// <summary>
    /// True when the add-note form created this source, false when an existing source was
    /// staged into the run. Only an import-created note may be deleted along with its item —
    /// removing a staged existing source must never touch the GM's real note.
    /// </summary>
    public bool CreatedByImport { get; set; }

    /// <summary>
    /// When the walk sent this item for extraction, or null while it still waits.
    ///
    /// Load-bearing for staged existing sources: they arrive already Processed, and state
    /// derived from status alone would call them Done before the walk ever reached them.
    /// Until an item is dispatched it is Waiting, whatever its source says.
    /// </summary>
    public DateTimeOffset? DispatchedAt { get; set; }

    // Navigation properties
    public ImportSession ImportSession { get; set; } = null!;
}
