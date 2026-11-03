using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Domain.Entities;

/// <summary>
/// An immutable uploaded document (sourcebook PDF, map image, handout) living in blob
/// storage. Deliberately not a <see cref="Source"/>: library documents are reference
/// material. A document as a whole is never extracted into canon; PDF text is chunked and
/// embedded so the Loremaster can quote it with page citations. What a GM may do is choose
/// pages and file them as a <see cref="SourceType.LibraryExcerpt"/> — the choosing is the
/// gate, and from there the excerpt is an ordinary source.
/// </summary>
public class LibraryDocument : ISlugged
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Per-world URL slug; see <see cref="ISlugged"/>.</summary>
    public string? Slug { get; set; }

    string ISlugged.SlugSource => Title;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string BlobPath { get; set; } = string.Empty;

    public LibraryDocumentKind Kind { get; set; }

    /// <summary>PartyVisible or GMOnly — Private has no meaning for shared reference shelves.</summary>
    public VisibilityScope Visibility { get; set; }

    public LibraryDocumentStatus Status { get; set; }

    public int? PageCount { get; set; }

    public int ChunkCount { get; set; }

    /// <summary>Why indexing failed, when <see cref="Status"/> is IndexFailed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The Adventure this document was printed from. Null means it isn't a Prep print, or its
    /// Adventure has since been deleted. Every print carries it: it is how a print is told
    /// apart from an uploaded handout.
    /// </summary>
    public Guid? AdventureId { get; set; }

    /// <summary>
    /// The Adventure an upload belongs to (feature 36): GM organisation that keeps one-shot
    /// handouts and maps off the Library's Shelf. Null means it belongs to none. Never set on a
    /// print, whose Adventure is <see cref="AdventureId"/>; set only by <c>LibraryService</c>.
    /// </summary>
    public Guid? LinkedAdventureId { get; set; }

    /// <summary>
    /// A print's details as they stood when it was filed (feature 32). Set once, by
    /// <c>LibraryService.RequestUploadAsync</c>, and never changed. Null means the document
    /// isn't a print, or was filed before prints carried details.
    /// </summary>
    public PrintDetails? PrintDetails { get; set; }

    /// <summary>Upload, or a book in the creator's Bibliotaph collection (feature 31).</summary>
    public LibraryDocumentOrigin Origin { get; set; }

    /// <summary>
    /// The book's entry in the Bibliotaph collection. Always set on a Bibliotaph row; on an
    /// upload, set once Bibliotaph has catalogued it, and null otherwise.
    /// </summary>
    public string? BibliotaphEntryId { get; set; }

    /// <summary>The document Bibliotaph's reader opens for this entry. Set with <see cref="BibliotaphEntryId"/>.</summary>
    public string? BibliotaphDocumentId { get; set; }

    /// <summary>
    /// The title Bibliotaph last sent. While <see cref="Title"/> equals it, a reconcile updates
    /// both; once the GM renames the row, only this one moves. Null on an uncatalogued upload.
    /// </summary>
    public string? RemoteTitle { get; set; }

    /// <summary>Bibliotaph's cover name for the book. Null means no cover; the page shows the kind's icon.</summary>
    public string? CoverName { get; set; }

    /// <summary>
    /// When the book left the Bibliotaph collection. Null means live. A removed row is hidden,
    /// unreadable and unsearchable, but kept, so excerpts filed from it keep their link.
    /// </summary>
    public DateTimeOffset? RemovedAt { get; set; }

    public Guid UploadedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;

    public User UploadedByUser { get; set; } = null!;

    public ICollection<LibraryChunk> Chunks { get; set; } = [];
}
