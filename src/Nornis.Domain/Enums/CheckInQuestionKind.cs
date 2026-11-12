namespace Nornis.Domain.Enums;

/// <summary>
/// What a check-in question is about (feature 44). The kind decides which answers it takes
/// (<c>CheckInAnswers.AllowedFor</c>) and what each answer does to the record.
/// </summary>
public enum CheckInQuestionKind
{
    /// <summary>Is the table pursuing this Active storyline, or is it background on someone?</summary>
    Storyline,

    /// <summary>Is this entry, created from one source and never mentioned again, worth keeping?</summary>
    Entry,

    /// <summary>Should Nornis keep recording this kind of detail (a predicate that recurs)?</summary>
    Pattern
}
