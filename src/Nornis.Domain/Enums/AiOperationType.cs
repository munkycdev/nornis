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
    AdventureConversation
}
