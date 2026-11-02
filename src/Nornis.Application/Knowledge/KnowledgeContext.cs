namespace Nornis.Application.Knowledge;

public class KnowledgeContext
{
    public required IReadOnlyList<KnowledgeArtifact> Artifacts { get; init; }
    public required IReadOnlyList<KnowledgeFact> Facts { get; init; }
    public required IReadOnlyList<KnowledgeRelationship> Relationships { get; init; }
    public required IReadOnlyList<KnowledgeSourceReference> SourceReferences { get; init; }

    /// <summary>Published-reference passages from the Library (attached by the Loremaster
    /// after keyword retrieval — kept separate from world canon).</summary>
    public IReadOnlyList<KnowledgePassage> Passages { get; init; } = [];

    /// <summary>True when the world's reference books (its Bibliotaph) couldn't be reached for
    /// this question, so the answer says it uses world memory only (feature 31).</summary>
    public bool ReferenceBooksUnavailable { get; init; }

    /// <summary>The world's most recent play sessions, newest first — the authoritative
    /// ground for time-anchored questions ("what happened last session?").</summary>
    public IReadOnlyList<KnowledgeSession> Sessions { get; init; } = [];
}
