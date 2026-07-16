namespace Nornis.Domain.Enums;

public enum SourceAttachmentStatus
{
    /// <summary>Row created and a write SAS issued; the blob has not been confirmed yet.</summary>
    PendingUpload,

    /// <summary>The blob's arrival was confirmed — the attachment is real.</summary>
    Stored
}
