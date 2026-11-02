namespace Nornis.Api.Contracts.Requests;

/// <summary>Find passages to file for a codex entry; <paramref name="Query"/> wins when given.</summary>
public record SearchLibraryExcerptsRequest(Guid? ArtifactId, string? Query);

/// <summary>
/// File passages of one document as an excerpt source — by passage ids, by ticked pages of a
/// Bibliotaph book, or by page range. Pages are 1-based.
/// </summary>
public record FileLibraryExcerptRequest(
    IReadOnlyList<Guid>? ChunkIds,
    int? PageFrom,
    int? PageTo,
    Guid? ArtifactId,
    IReadOnlyList<int>? Pages = null);
