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
}
