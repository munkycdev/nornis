using Nornis.Domain.Enums;

namespace Nornis.Application.Knowledge;

public class KnowledgeFact
{
    public required Guid Id { get; init; }
    public required Guid ArtifactId { get; init; }
    public required string Predicate { get; init; }
    public required string Value { get; init; }
    public required TruthState TruthState { get; init; }
    public required string ReferenceId { get; init; }
}
