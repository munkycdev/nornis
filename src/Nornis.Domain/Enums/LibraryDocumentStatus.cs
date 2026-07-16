namespace Nornis.Domain.Enums;

/// <summary>
/// Lifecycle of a library document. PendingUpload rows exist before the browser has
/// PUT the bytes to blob storage (the SAS handshake); PDFs then pass through
/// Indexing → Indexed/IndexFailed, while non-PDF files rest at Stored.
/// </summary>
public enum LibraryDocumentStatus
{
    PendingUpload,
    Stored,
    Indexing,
    Indexed,
    IndexFailed,
}
