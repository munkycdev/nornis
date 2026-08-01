using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// The one definition of an open question: a fact whose predicate is the reserved
/// "open question" — any casing, since predicates arrive both hand-typed and
/// AI-extracted — and that has not been marked False. Three services used to
/// re-declare this and one copy had drifted case-sensitive, silently dropping
/// "Open Question" rows from the retrospective prompt.
/// </summary>
public static class OpenQuestionFact
{
    public const string Predicate = "open question";

    public static bool IsOpenQuestion(ArtifactFact fact) =>
        HasOpenQuestionPredicate(fact) && fact.TruthState != TruthState.False;

    /// <summary>
    /// Name-match alone, truth state ignored — resolution semantics need this form:
    /// an open question marked False still must not settle to Confirmed.
    /// </summary>
    public static bool HasOpenQuestionPredicate(ArtifactFact fact) =>
        string.Equals(fact.Predicate, Predicate, StringComparison.OrdinalIgnoreCase);
}
