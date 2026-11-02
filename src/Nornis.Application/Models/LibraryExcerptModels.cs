using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// Find Library passages to file for a codex entry. <paramref name="Query"/> wins when given;
/// otherwise the artifact's name and summary are the query.
/// </summary>
public record SearchLibraryExcerptsCommand(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    Guid? ArtifactId,
    string? Query);

/// <summary>
/// File passages of one document as an excerpt source: by passage ids (an indexed upload's
/// chunks, from the artifact-page dialog), by ticked page numbers (a Bibliotaph book's pages,
/// from the same dialog), or by page range (the document page and Send to Nornis).
/// <paramref name="ArtifactId"/> names the codex entry the excerpt is filed for; it shapes the
/// title and the filing line and nothing else. Pages are 1-based.
/// </summary>
public record FileLibraryExcerptCommand(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    Guid DocumentId,
    IReadOnlyList<Guid> ChunkIds,
    int? PageFrom,
    int? PageTo,
    Guid? ArtifactId,
    IReadOnlyList<int>? Pages = null);

/// <summary>
/// One passage a GM may tick to file: an indexed chunk (<paramref name="ChunkId"/> set), or a
/// page of a Bibliotaph book (<paramref name="ChunkId"/> null), filed by its page number.
/// </summary>
public record LibraryExcerptCandidate(
    Guid? ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    int Page,
    string Text,
    string? PageLabel = null);

/// <summary>One page of a book's text. <paramref name="Page"/> is 1-based; <paramref name="Label"/> is the printed number, when known.</summary>
public record LibraryPage(int Page, string? Label, string Text);
