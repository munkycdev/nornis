namespace Nornis.Application.Ai;

public class FactContext
{
    /// <summary>The fact's UUID — the only valid targetId for an UpdateFact proposal.</summary>
    public required Guid Id { get; init; }
    public required string Predicate { get; init; }
    public required string Value { get; init; }
}
