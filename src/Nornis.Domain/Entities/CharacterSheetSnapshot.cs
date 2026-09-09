namespace Nornis.Domain.Entities;

/// <summary>
/// A dated photograph of a character's sheet, attached to an existing <see cref="Source"/>.
///
/// The bytes stay in the source ledger; this only says "that source is a picture of this
/// character's sheet, as it stood on this date". Detaching therefore never deletes anything,
/// and a snapshot is readable exactly when its source is — the feature adds no second
/// visibility rule for the same image.
/// </summary>
public class CharacterSheetSnapshot
{
    /// <summary>
    /// Ceiling on how many snapshots one character may keep. Attaching beyond it is refused
    /// rather than silently evicting the oldest, which would delete the history the feature
    /// exists to provide.
    /// </summary>
    public const int MaxSnapshots = 50;

    public Guid Id { get; set; }

    public Guid CharacterId { get; set; }

    public Guid SourceId { get; set; }

    /// <summary>
    /// When the sheet was current, which is not when the photograph was uploaded — a player
    /// catching up on three months of paper is recording history, not today.
    /// </summary>
    public DateTimeOffset AsOf { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    // Navigation properties
    public Character Character { get; set; } = null!;

    public Source Source { get; set; } = null!;
}
