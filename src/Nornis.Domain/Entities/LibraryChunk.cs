namespace Nornis.Domain.Entities;

/// <summary>
/// One retrievable passage of an indexed library document. The embedding vector is
/// deliberately NOT modeled here — it lives as an EF shadow property in Infrastructure
/// (SQL Server <c>vector(1536)</c>), keeping the domain free of provider types.
/// </summary>
public class LibraryChunk
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    /// <summary>Denormalized for world-wide similarity search without a join.</summary>
    public Guid WorldId { get; set; }

    /// <summary>Position of the chunk within the document (0-based).</summary>
    public int Ord { get; set; }

    /// <summary>1-based page the chunk starts on — powers "Title, p. N" citations.</summary>
    public int Page { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public LibraryDocument Document { get; set; } = null!;
}
