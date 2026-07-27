using System.Linq.Expressions;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>
/// The single definition of "may this reader see this source".
///
/// It exists as an <see cref="Expression"/> so the same rule can run in SQL (for counts and
/// projections, where materialising every row to filter in memory is the thing we are trying to
/// avoid) and in memory (for callers that already hold entities). A second, hand-written copy of
/// this predicate is how badge counts start disagreeing with list contents — and the direction
/// that disagreement fails is a Private note appearing in someone else's count.
///
/// Two gates, both of which must pass:
/// <list type="number">
///   <item>Scope — PartyVisible to everyone; Private to the GM or its author; GMOnly to the GM.</item>
///   <item>Draft — an unfinished source is visible only to the GM or its author, whatever its
///   scope. Capture's draft window is seconds, but a backlog import parks a whole backlog at
///   Draft for as long as the GM takes to walk it.</item>
/// </list>
///
/// <paramref name="userId"/> of <see cref="Guid.Empty"/> means "no identity" — the anonymous
/// public world page reads as Observer this way. An empty id must never match an ownership test,
/// or unattributable rows would leak to anonymous readers.
/// </summary>
public static class SourceVisibilityRule
{
    public static Expression<Func<Source, bool>> CanSee(Guid userId, WorldRole role)
    {
        // Hoisted to locals so EF renders them as SQL parameters rather than failing to
        // translate a captured method call.
        var isGm = role == WorldRole.GM;
        var hasIdentity = userId != Guid.Empty;

        return source =>
            // Gate 1: scope.
            (source.Visibility == VisibilityScope.PartyVisible
             || (source.Visibility == VisibilityScope.Private
                 && (isGm || (hasIdentity && source.CreatedByUserId == userId)))
             || (source.Visibility == VisibilityScope.GMOnly && isGm))
            // Gate 2: drafts are the author's and the GM's alone.
            && (source.ProcessingStatus != SourceProcessingStatus.Draft
                || isGm
                || (hasIdentity && source.CreatedByUserId == userId));
    }

    /// <summary>
    /// The same rule for callers holding entities in memory. Compile ONCE per call site and reuse
    /// the delegate across the collection — compiling inside a predicate would rebuild it per
    /// element, which is orders of magnitude more expensive than the comparison it performs.
    /// </summary>
    public static Func<Source, bool> Compile(Guid userId, WorldRole role) =>
        CanSee(userId, role).Compile();
}
