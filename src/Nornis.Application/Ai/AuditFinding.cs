namespace Nornis.Application.Ai;

/// <summary>
/// A single continuity finding as returned by the AI, before server-side validation. Category
/// and severity are carried as strings (exactly as the model emitted them) so the Application
/// service can validate them against the domain enums rather than the client guessing.
/// </summary>
public class AuditFinding
{
    public required string Category { get; init; }
    public required string Severity { get; init; }
    public required string Summary { get; init; }
    public string? SuggestedAction { get; init; }

    /// <summary>Reference ids the finding cites (e.g. "artifact:{guid}", "fact:{guid}", "rel:{guid}").</summary>
    public required IReadOnlyList<string> Evidence { get; init; }

    /// <summary>Optional primary artifact reference id for UI navigation.</summary>
    public string? ArtifactRef { get; init; }
}
