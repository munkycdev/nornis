namespace Nornis.Domain.Enums;

public enum AiOperationType
{
    SourceExtraction,
    ArtifactSummary,
    AskLoremaster,
    SourceExtractionRepair,
    ContinuityAudit,
    StorylineRetrospective,
    Embedding,
    RelationshipBackfill,
    HandwritingTranscription,
    ImageReading,
    MapExtraction,
    ContinuityFix,

    /// <summary>Naming a demo world at creation. Small, but the only AI call that used to
    /// leave no trace in the ledger at all.</summary>
    WorldNaming,
    WorldDigest,

    /// <summary>One campaign's "story so far" — the world digest's campaign-scoped sibling,
    /// metered separately so a GM can see what recapping a long campaign costs.</summary>
    CampaignRecap,

    /// <summary>Writing the why-now beside a convergence candidate. Annotates a ranking
    /// the system already computed; it never produces one.</summary>
    ConvergenceNarration,

    /// <summary>Drafting or revising one Prep section. Metered against the world's monthly
    /// Prep pool, never the daily world budget.</summary>
    AdventureDrafting,

    /// <summary>One turn of an Adventure's side conversation (feature 30). Metered apart from
    /// drafting so the GM can see what brainstorming costs, against the same monthly Prep pool.</summary>
    AdventureConversation,

    /// <summary>The Loremaster writing a cover's art brief (feature 38). A chat call, on Prep's model,
    /// against the monthly Prep pool.</summary>
    CoverArtBrief,

    /// <summary>Generating cover images from a brief (feature 38), on the key holder's image model,
    /// against the monthly Prep pool.</summary>
    CoverArt,

    /// <summary>Generating a world's key art from the GM's brief (feature 42), on the key holder's
    /// image model, against the monthly Prep pool, where the GM already sees creative spend.</summary>
    KeyArt,

    /// <summary>Refining one cover option (feature 45): the option sent as an edit to the key
    /// holder's refining model, against the monthly Prep pool.</summary>
    CoverArtRefinement,

    /// <summary>Refining one key-art option (feature 45), as <see cref="CoverArtRefinement"/>.</summary>
    KeyArtRefinement,

    /// <summary>Choosing and phrasing a check-in's questions from the evidence (feature 44). One
    /// call per check-in, on the Ask model, against the daily world budget.</summary>
    CheckIn
}
