using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>
/// A source as What you learned sorts and filters it: enough to decide whether it is newer than
/// a reader's marker and whether they may see it, and nothing the page renders from the source
/// itself — no body, no title. The digest asks for these on every nav-badge poll, and a
/// transcript's body is the one column that would make that poll expensive.
/// </summary>
/// <param name="OccurredAt">
/// When the source happened where that is recorded, and when it was written otherwise. This is
/// the date the reader's marker is compared against and the date the page sorts by, resolved
/// once in the query so the two cannot disagree.
/// </param>
public sealed record LearnedCandidate(
    Guid Id,
    SourceType Type,
    VisibilityScope Visibility,
    Guid CreatedByUserId,
    DateTimeOffset OccurredAt,
    string? RevealNote);
