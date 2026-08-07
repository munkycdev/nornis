namespace Nornis.Application.Services;

/// <summary>
/// Every constant the convergence score depends on, in one place — the read model carries the
/// total so no caller ever recomputes it, and tuning is one file. These weights are a starting
/// guess, not a finding: they want one real world's worth of use before anyone defends a
/// number (see docs/features/21-convergence-gauge/design.md).
/// </summary>
public static class ConvergenceWeights
{
    /// <summary>The party believes something the hidden record contradicts — a reveal with a
    /// deadline, so it outweighs every other signal.</summary>
    public const double Contradiction = 0.40;

    /// <summary>How much a reveal drags with it. Cheap reveals are the ones a GM can act on
    /// between sessions.</summary>
    public const double SelfContainment = 0.25;

    /// <summary>How long the secret has sat untouched.</summary>
    public const double Dormancy = 0.20;

    /// <summary>Whether the fiction around it has finished.</summary>
    public const double Storyline = 0.15;

    /// <summary>
    /// Familiarity multiplies the weighted sum rather than joining it, so a secret on an entity
    /// the party has never met cannot climb on age alone. The floor keeps such a secret ranked
    /// low rather than erased — "not yet legible to anyone" is not the same as "not a secret".
    /// </summary>
    public const double FamiliarityFloor = 0.15;

    /// <summary>Six months of silence is as dormant as the score needs to say. A two-year-old
    /// secret is not twice as ripe as a one-year-old one.</summary>
    public const int DormancySaturationDays = 180;

    /// <summary>Party-visible facts on the anchoring artifact past which the party plainly
    /// knows the entity.</summary>
    public const int FamiliaritySaturationFacts = 5;

    /// <summary>
    /// The gauge is a shortlist, not an inventory. A world's hidden material is bounded by
    /// nothing, and a GM handed nine hundred rows has been handed the flat list this feature
    /// exists to replace.
    /// </summary>
    public const int MaxCandidates = 50;

    /// <summary>
    /// Facts loaded per artifact. Known limitation, recorded rather than hidden: the repository
    /// returns the *newest* facts per artifact, and dormancy ranks the *oldest* — so on an
    /// artifact carrying more facts than this, the truncation drops exactly the candidates the
    /// gauge most wants. Set high enough that a real artifact does not reach it. The fix, if a
    /// world ever does, is a query for hidden facts by world rather than a bigger number here.
    /// </summary>
    public const int MaxFactsPerArtifact = 200;
}
