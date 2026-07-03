namespace Nornis.Application.Knowledge;

public class Citation
{
    public required string ReferenceId { get; init; }
    public required CitationType Type { get; init; }
    public required string DisplayName { get; init; }
    public Guid? ArtifactId { get; init; }
    public Guid? FactId { get; init; }
    public Guid? RelationshipId { get; init; }
    public Guid? SourceId { get; init; }
}
