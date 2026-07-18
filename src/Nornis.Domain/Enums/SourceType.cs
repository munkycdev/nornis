namespace Nornis.Domain.Enums;

public enum SourceType
{
    SessionNote,

    /// <summary>Legacy — retired from the capture UI; existing sources keep it.</summary>
    JournalEntry,

    /// <summary>Legacy — retired from the capture UI; existing sources keep it.</summary>
    Transcript,

    Upload,
    Image,
    HandwrittenNotes,
    WebLink,
    GMNote,
    ImportedNote,
    SessionAudio,
    FanFiction
}
