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
/// File passages of one document as an excerpt source: by passage ids (the artifact-page
/// dialog), or by page range (the document page). <paramref name="ArtifactId"/> names the
/// codex entry the excerpt is filed for; it shapes the title and the filing line and nothing
/// else.
/// </summary>
public record FileLibraryExcerptCommand(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    Guid DocumentId,
    IReadOnlyList<Guid> ChunkIds,
    int? PageFrom,
    int? PageTo,
    Guid? ArtifactId);

/// <summary>One passage a GM may tick to file.</summary>
public record LibraryExcerptCandidate(
    Guid ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    int Page,
    string Text);
