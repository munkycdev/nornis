namespace Nornis.Application.Ai;

public class ArtifactContext
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<FactContext> Facts { get; init; } = [];
}
