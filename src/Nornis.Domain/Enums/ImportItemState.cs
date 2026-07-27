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
    Waiting = 0,

    /// <summary>Marked ready — queued, or in the worker's hands.</summary>
    Extracting = 1,

    /// <summary>Extracted; open (Pending or Edited) proposals still await a decision.</summary>
    Reviewing = 2,

    /// <summary>Extraction failed. The GM can retry it or skip it.</summary>
    Failed = 3,

    /// <summary>Processed with nothing left open — the walk may advance past it.</summary>
    Done = 4,

    /// <summary>Passed over by the GM. Overrides every other state.</summary>
    Skipped = 5
}
