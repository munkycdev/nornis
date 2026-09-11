using Nornis.Domain.Enums;

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

    public Guid UploadedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;

    public User UploadedByUser { get; set; } = null!;

    public ICollection<LibraryChunk> Chunks { get; set; } = [];
}
