namespace Nornis.Domain.Enums;

/// <summary>
/// The GM's answer to a check-in question (feature 44). Which values a question takes depends
/// on its <see cref="CheckInQuestionKind"/>; <c>CheckInAnswers.AllowedFor</c> is the one table.
/// </summary>
public enum CheckInAnswer
{
    /// <summary>Storyline: the table is pursuing it. Nothing changes.</summary>
    Pursuing,

    /// <summary>Storyline: it is background on a character or faction. Merged into that entry when one is named, else Dormant.</summary>
    Background,

    /// <summary>Storyline: it concluded. Resolved.</summary>
    Done,

    /// <summary>Entry or Pattern: keep it. Nothing changes.</summary>
    Keep,

    /// <summary>Entry: set it Archived. The entry page can undo it.</summary>
    Archive,

    /// <summary>Pattern: stop recording this kind of detail. Writes a standing instruction; existing facts stand.</summary>
    Stop,

    /// <summary>Any kind: no answer. Nothing changes, and the question is not asked again this check-in.</summary>
    Skip
}
