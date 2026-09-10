using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>What kinds of thing the quick switcher can jump to, in the order its groups render.</summary>
public enum JumpKind
{
    Campaign,
    Character,
    Artifact,
    Session,
    Library,
}

public record JumpQuery(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    string Term,
    int PerKind = 8);

/// <param name="Detail">A word or two beside the name: an artifact's type, a campaign's status, a session's date.</param>
public record JumpItem(Guid Id, string Name, string? Detail);

/// <param name="TotalCount">
/// Matches of this kind the reader may see, before the per-kind cap — so the UI can say "8 of
/// 23" and link to the kind's own page. Counts only what the reader may see, so it can never
/// become a count of withheld material.
/// </param>
public record JumpGroup(JumpKind Kind, IReadOnlyList<JumpItem> Items, int TotalCount);

public record JumpResult(IReadOnlyList<JumpGroup> Groups);
