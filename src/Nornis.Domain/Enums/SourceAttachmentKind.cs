namespace Nornis.Domain.Enums;

public enum SourceAttachmentKind
{
    /// <summary>A page image of handwritten notes — the input to vision transcription.</summary>
    PageImage,

    /// <summary>The ink-canvas stroke document (JSON) — the true source for in-app handwriting.</summary>
    InkDocument,

    /// <summary>An image attached to an Image source — vision-read for lore at extraction.</summary>
    ImageFile,

    /// <summary>A file attached to an Upload source (PDF / text / markdown / image).</summary>
    Document,

    /// <summary>The single map image on a Map source — the input to map extraction.</summary>
    MapImage
}
