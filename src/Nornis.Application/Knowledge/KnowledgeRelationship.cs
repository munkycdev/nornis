using Nornis.Domain.Enums;

namespace Nornis.Application.Knowledge;

public class KnowledgeRelationship
{
    public required Guid Id { get; init; }
    public required Guid ArtifactAId { get; init; }
    public required Guid ArtifactBId { get; init; }
    public required string Type { get; init; }
    public string? Description { get; init; }
    public required TruthState TruthState { get; init; }
    public required string ReferenceId { get; init; }
}
