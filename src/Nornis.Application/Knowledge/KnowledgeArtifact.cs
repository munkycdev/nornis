namespace Nornis.Application.Knowledge;

public class KnowledgeArtifact
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Summary { get; init; }
    public required string ReferenceId { get; init; }
}
