using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>
/// The one place that decides which source types a member may write through ordinary
/// capture — creating a source, or retyping one.
///
/// A GM note is the GM's own voice in the record: extraction reads it as the GM correcting
/// canon, so a Player who could file one could rewrite the world in the GM's name. Reveal,
/// LibraryExcerpt and AdventureExport are minted by their own services with provenance capture
/// cannot supply (the disclosed elements, the document and page range, the Adventure), so no
/// role captures them, and a source already of one of those types keeps it.
/// </summary>
public static class SourceCapture
{
    /// <summary>Types only their own service creates.</summary>
    public static bool IsMinted(SourceType type) =>
        type is SourceType.Reveal or SourceType.LibraryExcerpt or SourceType.AdventureExport;

    /// <summary>Types only a GM may capture.</summary>
    public static bool IsGmOnly(SourceType type) =>
        type is SourceType.GMNote;
}
