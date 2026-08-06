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
    WorldDigest
}
