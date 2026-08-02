namespace Nornis.Domain.Enums;

public enum ImportSessionStatus
{
    /// <summary>Being assembled: notes are being pasted in and ordered, nothing has extracted yet.</summary>
    Draft,

    /// <summary>Walking the backlog: one note at a time, oldest first, paused for review between them.</summary>
    InProgress,

    /// <summary>Every item is done or skipped.</summary>
    Completed,

    /// <summary>Stopped by the GM. Nothing is deleted — processed notes keep their knowledge
    /// and still-held notes remain ordinary draft sources.</summary>
    Abandoned
}
