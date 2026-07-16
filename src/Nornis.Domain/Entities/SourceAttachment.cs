using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// A blob-backed file hanging off a <see cref="Source"/> — page images of handwritten
/// notes, or the ink-canvas stroke document. Uploads use the same two-phase SAS handshake
/// as library documents (PendingUpload → Stored).
/// </summary>
public class SourceAttachment
{
    public Guid Id { get; set; }

    public Guid SourceId { get; set; }

    public Guid WorldId { get; set; }

    public SourceAttachmentKind Kind { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string BlobPath { get; set; } = string.Empty;

    /// <summary>Page order for PageImage attachments; 0 for the single InkDocument.</summary>
    public int Ord { get; set; }

    public SourceAttachmentStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation properties
    public Source Source { get; set; } = null!;
}
