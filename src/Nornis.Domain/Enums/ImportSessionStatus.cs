namespace Nornis.Domain.Enums;

public enum ImportSessionStatus
{
    /// <summary>Being assembled: notes are being pasted in and ordered, nothing has extracted yet.</summary>
    Draft = 0,

    /// <summary>Walking the backlog: one note at a time, oldest first, paused for review between them.</summary>
    InProgress = 1,

    /// <summary>Every item is done or skipped.</summary>
    Completed = 2,

    /// <summary>Stopped by the GM. Nothing is deleted — processed notes keep their knowledge
    /// and still-held notes remain ordinary draft sources.</summary>
    Abandoned = 3
}
