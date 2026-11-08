namespace Nornis.Application.Ai;

/// <summary>
/// The model's judgment of what the table will do with a proposal (feature 43). Distinct from
/// confidence, which says how sure the model is that the source establishes it: Symbaroum's
/// review history showed the two piles Dave kept and rejected had the same confidence, because
/// the model was right that the source said it and nobody had asked whether it mattered.
/// </summary>
public enum ProposalSignificance
{
    /// <summary>Mentioned once with no consequence. Dropped by <see cref="ExtractionTriage"/>
    /// before anything is filed; the source keeps the text.</summary>
    Incidental,

    /// <summary>Texture on something Core: a detail a GM would want to recall when the thing it
    /// describes next comes up. Also what a proposal is when the model did not say.</summary>
    Supporting,

    /// <summary>The party acted on it or will have to; it changes what they do next; it names
    /// someone they will meet again; it answers or raises a question they are pursuing.</summary>
    Core
}
