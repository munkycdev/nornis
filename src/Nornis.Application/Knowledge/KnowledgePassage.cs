namespace Nornis.Application.Knowledge;

/// <summary>A published-reference passage retrieved for a question — rulebook or module
/// text, kept distinct from world canon in the prompt and citations.</summary>
public class KnowledgePassage
{
    public required Guid ChunkId { get; init; }
    public required Guid DocumentId { get; init; }
    public required string DocumentTitle { get; init; }
    public required int Page { get; init; }
    public required string Text { get; init; }
    public required string ReferenceId { get; init; }
}

/// <summary>Vector retrieval over indexed library documents; empty when the world has none.</summary>
public interface IReferencePassageRetriever
{
    Task<IReadOnlyList<KnowledgePassage>> RetrieveAsync(
        string question,
        Guid worldId,
        Guid userId,
        Domain.Enums.WorldRole role,
        CancellationToken ct);

    /// <summary>
    /// Retrieves passages for a caller identified by visibility scopes rather than a world
    /// role — used by extraction, which grounds against a source's <see cref="Domain.Enums.VisibilityScope"/>
    /// rather than a user. <paramref name="attributedUserId"/> attributes the embedding cost.
    /// </summary>
    Task<IReadOnlyList<KnowledgePassage>> RetrieveForScopesAsync(
        string query,
        Guid worldId,
        IReadOnlyList<Domain.Enums.VisibilityScope> allowedScopes,
        Guid? attributedUserId,
        CancellationToken ct);
}
