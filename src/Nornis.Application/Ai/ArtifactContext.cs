namespace Nornis.Application.Ai;

public class ArtifactContext
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<FactContext> Facts { get; init; } = [];

    /// <summary>
    /// The artifact's <c>ArtifactStatus</c> as a string, or null when the caller does not know
    /// it (the eval runner's stand-in context). Rendered only when it is not Active, so a
    /// retired storyline reads as retired and a live one costs no extra line (feature 43).
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// For storylines nested in the GM's hierarchy: the name of the parent storyline
    /// (the "PartOf" relationship). Shown in the prompt so the model grounds its own
    /// PartOf proposals in the curated tree and never re-proposes existing links.
    /// </summary>
    public string? PartOfName { get; init; }
}
