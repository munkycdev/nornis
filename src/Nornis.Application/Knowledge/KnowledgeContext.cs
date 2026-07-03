namespace Nornis.Application.Knowledge;

public class KnowledgeContext
{
    public required IReadOnlyList<KnowledgeArtifact> Artifacts { get; init; }
    public required IReadOnlyList<KnowledgeFact> Facts { get; init; }
    public required IReadOnlyList<KnowledgeRelationship> Relationships { get; init; }
    public required IReadOnlyList<KnowledgeSourceReference> SourceReferences { get; init; }
}
