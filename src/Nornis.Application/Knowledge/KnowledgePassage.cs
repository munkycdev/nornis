namespace Nornis.Application.Knowledge;

/// <summary>A published-reference passage retrieved for a question — rulebook or module
/// text, kept distinct from world canon in the prompt and citations.</summary>
public class KnowledgePassage
{
    /// <summary>The indexed chunk, for an upload Nornis indexed; null for a page from Bibliotaph (feature 31).</summary>
    public required Guid? ChunkId { get; init; }
    public required Guid DocumentId { get; init; }
    public required string DocumentTitle { get; init; }
    /// <summary>1-based page number in the file.</summary>
    public required int Page { get; init; }

    /// <summary>The printed page number, when Bibliotaph knows it. What a citation shows.</summary>
    public string? PageLabel { get; init; }

    /// <summary>The page as a citation names it: the printed label, else the page number.</summary>
    public string PageShown => string.IsNullOrWhiteSpace(PageLabel) ? Page.ToString(System.Globalization.CultureInfo.InvariantCulture) : PageLabel;
    public required string Text { get; init; }
    public required string ReferenceId { get; init; }

    public string? DocumentSlug { get; init; }
}

/// <summary>
/// What a reference retrieval found. <see cref="Unavailable"/> means the reference books couldn't
/// be reached (Bibliotaph timed out or refused), so the answer uses world memory only and says so.
/// </summary>
public sealed record ReferencePassages(IReadOnlyList<KnowledgePassage> Passages, bool Unavailable = false)
{
    public static readonly ReferencePassages None = new([]);
}

/// <summary>
/// How closely a reference passage must match. <see cref="Ask"/> is a question being answered:
/// a weak match is no match. <see cref="Search"/> is a GM choosing pages to file: the closest
/// pages come back anyway. Bibliotaph's <c>passages</c> modes; the vector retriever ranks the
/// same either way.
/// </summary>
public enum ReferenceSearchMode
{
    Ask,
    Search,
}

/// <summary>Retrieval over the world's reference books; empty when the world has none.</summary>
public interface IReferencePassageRetriever
{
    Task<ReferencePassages> RetrieveAsync(
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
    Task<ReferencePassages> RetrieveForScopesAsync(
        string query,
        Guid worldId,
        IReadOnlyList<Domain.Enums.VisibilityScope> allowedScopes,
        Guid? attributedUserId,
        ReferenceSearchMode mode,
        CancellationToken ct);
}
