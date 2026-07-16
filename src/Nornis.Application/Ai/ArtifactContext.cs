namespace Nornis.Application.Ai;

public class ArtifactContext
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<FactContext> Facts { get; init; } = [];

    /// <summary>
    /// For storylines nested in the GM's hierarchy: the name of the parent storyline
    /// (the "PartOf" relationship). Shown in the prompt so the model grounds its own
    /// PartOf proposals in the curated tree and never re-proposes existing links.
    /// </summary>
    public string? PartOfName { get; init; }
}
