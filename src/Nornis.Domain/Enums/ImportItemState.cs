namespace Nornis.Domain.Enums;

/// <summary>
/// Where an import item stands. Derived entirely from its source's processing status and
/// its open proposal count — the item row carries no status of its own beyond
/// <see cref="Entities.ImportSessionItem.Skipped"/>, so the walk can never disagree with
/// the record.
/// </summary>
public enum ImportItemState
{
    /// <summary>Held back: the source is still Draft, waiting its turn.</summary>
    Waiting,

    /// <summary>Marked ready — queued, or in the worker's hands.</summary>
    Extracting,

    /// <summary>Extracted; open (Pending or Edited) proposals still await a decision.</summary>
    Reviewing,

    /// <summary>Extraction failed. The GM can retry it or skip it.</summary>
    Failed,

    /// <summary>Processed with nothing left open — the walk may advance past it.</summary>
    Done,

    /// <summary>Passed over by the GM. Overrides every other state.</summary>
    Skipped
}
