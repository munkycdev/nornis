namespace Nornis.Domain.Enums;

/// <summary>
/// What became of one suggested edit in a Design turn's answer (feature 41), which the server
/// applies to the design draft as the answer ends.
/// </summary>
public enum DraftEditOutcome
{
    /// <summary>The edit is in the draft: applied with the answer, or by hand afterwards.</summary>
    Applied,

    /// <summary>Its passage wasn't in the draft exactly once, so it was left out; the GM can still apply it by hand.</summary>
    DidNotFit,

    /// <summary>
    /// Nothing from the answer was applied: the draft changed while it was written, or the result
    /// would have been longer than a draft may be. The GM can still apply it by hand.
    /// </summary>
    NotApplied,
}
