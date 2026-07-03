namespace Nornis.Application.Knowledge;

public class KnowledgeSourceReference
{
    public required Guid Id { get; init; }
    public required Guid SourceId { get; init; }
    public required Guid TargetId { get; init; }
    public string? Quote { get; init; }
    public required string ReferenceId { get; init; }
}
