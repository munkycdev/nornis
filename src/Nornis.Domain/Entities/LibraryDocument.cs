using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// An immutable uploaded document (sourcebook PDF, map image, handout) living in blob
/// storage. Deliberately not a <see cref="Source"/>: library documents are reference
/// material — never extracted into canon, but PDF text is chunked and embedded so the
/// Loremaster can quote them with page citations.
/// </summary>
public class LibraryDocument
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public string Title { get; set; } = string.Empty;

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
