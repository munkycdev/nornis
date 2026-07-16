namespace Nornis.Domain.Enums;

public enum SourceAttachmentKind
{
    /// <summary>A page image of handwritten notes — the input to vision transcription.</summary>
    PageImage,

    /// <summary>The ink-canvas stroke document (JSON) — the true source for in-app handwriting.</summary>
    InkDocument
}
